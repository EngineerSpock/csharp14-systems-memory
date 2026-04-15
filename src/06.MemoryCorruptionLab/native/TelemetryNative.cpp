#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "TelemetryNative.h"

#include <intrin.h>
#include <memory.h>

struct tm_packet_buffer
{
    uint32_t magic;
    uint32_t capacity;
    uint8_t* data;
};

struct tm_frame_trailer
{
    uint64_t cookie;
    uint64_t token;
};

struct tm_pending_frame
{
    tm_pending_frame* next;
    uint8_t* frame;
    uint32_t frame_length;
    uint32_t copied_length;
    uint64_t token;
    uint32_t ownership;
    tm_frame_trailer* trailer;
};

struct tm_session
{
    CRITICAL_SECTION lock;
    HANDLE work_event;
    HANDLE drained_event;
    HANDLE worker_thread;
    volatile LONG stop_requested;
    volatile LONG in_flight;
    tm_pending_frame* head;
    tm_pending_frame* tail;
    tm_completion_callback callback;
    void* callback_context;
    tm_session_options options;
};

namespace
{
    constexpr uint32_t TM_WIRE_MAGIC = 0x314D4C54u; // "TLM1"
    constexpr uint32_t TM_PACKET_BUFFER_MAGIC = 0x52464642u; // "BFFR"
    constexpr uint64_t TM_TRAILER_COOKIE = 0x544D475541524431ull; // "TMGUARD1"

    enum tm_frame_ownership : uint32_t
    {
        TM_FRAME_OWNED_COPY = 1,
        TM_FRAME_EXTERNAL_ZERO_COPY = 2
    };

    uint32_t tm_compute_checksum_core(const uint8_t* data, uint32_t length)
    {
        uint32_t checksum = 2166136261u;

        for (uint32_t i = 0; i < length; ++i)
        {
            checksum ^= data[i];
            checksum *= 16777619u;
            checksum = _rotl(checksum, 5) ^ (i + 1u);
        }

        return checksum;
    }

    bool tm_queue_empty(tm_session* session)
    {
        bool is_empty = false;

        EnterCriticalSection(&session->lock);
        is_empty = session->head == nullptr;
        LeaveCriticalSection(&session->lock);

        return is_empty;
    }

    void tm_enqueue(tm_session* session, tm_pending_frame* pending)
    {
        EnterCriticalSection(&session->lock);

        pending->next = nullptr;
        if (session->tail != nullptr)
        {
            session->tail->next = pending;
        }
        else
        {
            session->head = pending;
        }

        session->tail = pending;
        InterlockedIncrement(&session->in_flight);
        ResetEvent(session->drained_event);

        LeaveCriticalSection(&session->lock);

        SetEvent(session->work_event);
    }

    tm_pending_frame* tm_try_dequeue(tm_session* session)
    {
        EnterCriticalSection(&session->lock);

        tm_pending_frame* pending = session->head;
        if (pending != nullptr)
        {
            session->head = pending->next;
            if (session->head == nullptr)
            {
                session->tail = nullptr;
            }
        }

        LeaveCriticalSection(&session->lock);
        return pending;
    }

    void tm_invoke_callback(tm_session* session, const tm_completion* completion)
    {
        tm_completion_callback callback = session->callback;
        if (callback == nullptr)
        {
            return;
        }

        callback(session->callback_context, completion);
    }

    void tm_complete_pending(tm_session* session, tm_pending_frame* pending, const tm_completion& completion)
    {
        tm_invoke_callback(session, &completion);

        if (pending->ownership == TM_FRAME_OWNED_COPY)
        {
            HeapFree(GetProcessHeap(), 0, pending);
        }
        else
        {
            HeapFree(GetProcessHeap(), 0, pending);
        }

        const LONG remaining = InterlockedDecrement(&session->in_flight);
        if (remaining == 0 && tm_queue_empty(session))
        {
            SetEvent(session->drained_event);
        }
    }

    void tm_process_pending(tm_session* session, tm_pending_frame* pending)
    {
        if (session->options.worker_delay_ms != 0)
        {
            Sleep(session->options.worker_delay_ms);
        }

        tm_completion completion = {};
        completion.token = pending->token;
        completion.submitted_length = pending->frame_length;
        completion.copied_length = pending->copied_length;
        completion.frame_address = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(pending->frame));

        if (pending->trailer != nullptr && pending->trailer->cookie != TM_TRAILER_COOKIE)
        {
            if (IsDebuggerPresent())
            {
                DebugBreak();
            }

            completion.status = TM_STATUS_TRAILER_CORRUPTED;
            completion.note1 = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(pending->trailer));
            completion.note2 = pending->trailer->cookie;
            tm_complete_pending(session, pending, completion);
            return;
        }

        tm_packet_inspection inspection = {};
        const int32_t inspect_result = tm_inspect_frame(pending->frame, pending->frame_length, &inspection);
        if (inspect_result != 0)
        {
            if (IsDebuggerPresent())
            {
                DebugBreak();
            }

            completion.status = TM_STATUS_BAD_HEADER;
            completion.note1 = static_cast<uint64_t>(inspect_result);
            tm_complete_pending(session, pending, completion);
            return;
        }

        auto* header = reinterpret_cast<tm_wire_header*>(pending->frame);
        auto* payload = reinterpret_cast<uint8_t*>(pending->frame) + header->header_size;
        const uint32_t observed_checksum = tm_compute_checksum_core(payload, header->payload_length);

        completion.observed_checksum = observed_checksum;
        completion.status = observed_checksum == header->payload_checksum
            ? TM_STATUS_OK
            : TM_STATUS_CHECKSUM_MISMATCH;

        // The worker mutates the frame in place to model a real processing pipeline.
        // On the zero-copy path this becomes the delayed use-after-free write when the
        // caller released the backing buffer too early.
        header->flags |= 0x8000u;

        tm_complete_pending(session, pending, completion);
    }

    DWORD WINAPI tm_worker_thread_entry(void* parameter)
    {
        auto* session = static_cast<tm_session*>(parameter);

        for (;;)
        {
            const DWORD wait_result = WaitForSingleObject(session->work_event, INFINITE);
            if (wait_result != WAIT_OBJECT_0)
            {
                return 1;
            }

            for (;;)
            {
                tm_pending_frame* pending = tm_try_dequeue(session);
                if (pending == nullptr)
                {
                    break;
                }

                tm_process_pending(session, pending);
            }

            if (session->stop_requested != 0 && tm_queue_empty(session))
            {
                return 0;
            }
        }
    }

    int32_t tm_validate_header_bounds(const tm_wire_header* header, uint32_t frame_length)
    {
        if (header == nullptr)
        {
            return TM_STATUS_BAD_ARGUMENT;
        }

        if (frame_length < sizeof(tm_wire_header))
        {
            return TM_STATUS_BAD_HEADER;
        }

        if (header->magic != TM_WIRE_MAGIC)
        {
            return TM_STATUS_BAD_HEADER;
        }

        if (header->header_size != sizeof(tm_wire_header))
        {
            return TM_STATUS_BAD_HEADER;
        }

        if (header->payload_length > frame_length - header->header_size)
        {
            return TM_STATUS_BAD_HEADER;
        }

        return 0;
    }
}

TM_API int32_t TM_CALL tm_session_open(const tm_session_options* options, tm_session** out_session)
{
    if (out_session == nullptr || options == nullptr || options->struct_size != sizeof(tm_session_options))
    {
        return TM_STATUS_BAD_ARGUMENT;
    }

    auto* session = static_cast<tm_session*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(tm_session)));
    if (session == nullptr)
    {
        return TM_STATUS_INTERNAL_ERROR;
    }

    session->options = *options;
    if (session->options.worker_delay_ms == 0)
    {
        session->options.worker_delay_ms = 120;
    }

    InitializeCriticalSection(&session->lock);

    session->work_event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    session->drained_event = CreateEventW(nullptr, TRUE, TRUE, nullptr);

    if (session->work_event == nullptr || session->drained_event == nullptr)
    {
        if (session->work_event != nullptr)
        {
            CloseHandle(session->work_event);
        }

        if (session->drained_event != nullptr)
        {
            CloseHandle(session->drained_event);
        }

        DeleteCriticalSection(&session->lock);
        HeapFree(GetProcessHeap(), 0, session);
        return TM_STATUS_INTERNAL_ERROR;
    }

    session->worker_thread = CreateThread(nullptr, 0, tm_worker_thread_entry, session, 0, nullptr);
    if (session->worker_thread == nullptr)
    {
        CloseHandle(session->work_event);
        CloseHandle(session->drained_event);
        DeleteCriticalSection(&session->lock);
        HeapFree(GetProcessHeap(), 0, session);
        return TM_STATUS_INTERNAL_ERROR;
    }

    *out_session = session;
    return 0;
}

TM_API void TM_CALL tm_session_close(tm_session* session)
{
    if (session == nullptr)
    {
        return;
    }

    session->stop_requested = 1;
    SetEvent(session->work_event);
    WaitForSingleObject(session->worker_thread, INFINITE);

    tm_pending_frame* pending = nullptr;
    while ((pending = tm_try_dequeue(session)) != nullptr)
    {
        HeapFree(GetProcessHeap(), 0, pending);
    }

    CloseHandle(session->worker_thread);
    CloseHandle(session->work_event);
    CloseHandle(session->drained_event);
    DeleteCriticalSection(&session->lock);
    HeapFree(GetProcessHeap(), 0, session);
}

TM_API int32_t TM_CALL tm_session_register_callback(tm_session* session, tm_completion_callback callback, void* context)
{
    if (session == nullptr || callback == nullptr)
    {
        return TM_STATUS_BAD_ARGUMENT;
    }

    session->callback = callback;
    session->callback_context = context;
    return 0;
}

TM_API int32_t TM_CALL tm_session_submit_copy(tm_session* session, const void* frame, uint32_t frame_length, uint64_t token)
{
    if (session == nullptr || frame == nullptr || frame_length < sizeof(tm_wire_header))
    {
        return TM_STATUS_BAD_ARGUMENT;
    }

    const SIZE_T allocation_size = sizeof(tm_pending_frame) + frame_length + sizeof(tm_frame_trailer);
    auto* pending = static_cast<tm_pending_frame*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, allocation_size));
    if (pending == nullptr)
    {
        return TM_STATUS_INTERNAL_ERROR;
    }

    pending->frame = reinterpret_cast<uint8_t*>(pending + 1);
    pending->frame_length = frame_length;
    pending->copied_length = frame_length;
    pending->token = token;
    pending->ownership = TM_FRAME_OWNED_COPY;
    pending->trailer = reinterpret_cast<tm_frame_trailer*>(pending->frame + frame_length);
    pending->trailer->cookie = TM_TRAILER_COOKIE;
    pending->trailer->token = token;

    uint32_t copy_length = frame_length;

    if (session->options.demo_mode == TM_DEMO_LAYOUT_OVERFLOW)
    {
        const auto* buggy_header = static_cast<const tm_buggy_header_layout*>(frame);
        copy_length = static_cast<uint32_t>(sizeof(tm_buggy_header_layout) + buggy_header->payload_length);
        if (copy_length > frame_length + sizeof(tm_frame_trailer))
        {
            copy_length = frame_length + sizeof(tm_frame_trailer);
        }
    }

    pending->copied_length = copy_length;
    memcpy(pending->frame, frame, copy_length);

    tm_enqueue(session, pending);
    return 0;
}

TM_API int32_t TM_CALL tm_session_submit_zero_copy(tm_session* session, void* frame, uint32_t frame_length, uint64_t token)
{
    if (session == nullptr || frame == nullptr || frame_length < sizeof(tm_wire_header))
    {
        return TM_STATUS_BAD_ARGUMENT;
    }

    auto* pending = static_cast<tm_pending_frame*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(tm_pending_frame)));
    if (pending == nullptr)
    {
        return TM_STATUS_INTERNAL_ERROR;
    }

    pending->frame = static_cast<uint8_t*>(frame);
    pending->frame_length = frame_length;
    pending->copied_length = frame_length;
    pending->token = token;
    pending->ownership = TM_FRAME_EXTERNAL_ZERO_COPY;
    pending->trailer = nullptr;

    tm_enqueue(session, pending);
    return 0;
}

TM_API int32_t TM_CALL tm_session_allocate_packet_buffer(tm_session* session, uint32_t capacity, tm_packet_buffer** out_buffer)
{
    if (session == nullptr || out_buffer == nullptr || capacity < sizeof(tm_wire_header))
    {
        return TM_STATUS_BAD_ARGUMENT;
    }

    const SIZE_T allocation_size = sizeof(tm_packet_buffer) + capacity;
    auto* buffer = static_cast<tm_packet_buffer*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, allocation_size));
    if (buffer == nullptr)
    {
        return TM_STATUS_INTERNAL_ERROR;
    }

    buffer->magic = TM_PACKET_BUFFER_MAGIC;
    buffer->capacity = capacity;
    buffer->data = reinterpret_cast<uint8_t*>(buffer + 1);

    *out_buffer = buffer;
    return 0;
}

TM_API void* TM_CALL tm_packet_buffer_data(tm_packet_buffer* buffer)
{
    if (buffer == nullptr || buffer->magic != TM_PACKET_BUFFER_MAGIC)
    {
        return nullptr;
    }

    return buffer->data;
}

TM_API uint32_t TM_CALL tm_packet_buffer_capacity(const tm_packet_buffer* buffer)
{
    if (buffer == nullptr || buffer->magic != TM_PACKET_BUFFER_MAGIC)
    {
        return 0;
    }

    return buffer->capacity;
}

TM_API void TM_CALL tm_packet_buffer_release(tm_packet_buffer* buffer)
{
    if (buffer == nullptr)
    {
        return;
    }

    const uint32_t capacity = buffer->capacity;
    memset(buffer, 0xDD, sizeof(tm_packet_buffer) + capacity);
    HeapFree(GetProcessHeap(), 0, buffer);
}

TM_API uint32_t TM_CALL tm_compute_checksum(const void* data, uint32_t length)
{
    if (data == nullptr && length != 0)
    {
        return 0;
    }

    return tm_compute_checksum_core(static_cast<const uint8_t*>(data), length);
}

TM_API int32_t TM_CALL tm_inspect_frame(const void* frame, uint32_t frame_length, tm_packet_inspection* inspection)
{
    if (frame == nullptr || inspection == nullptr)
    {
        return TM_STATUS_BAD_ARGUMENT;
    }

    const auto* header = static_cast<const tm_wire_header*>(frame);
    const int32_t validation_result = tm_validate_header_bounds(header, frame_length);
    if (validation_result != 0)
    {
        return validation_result;
    }

    inspection->status = TM_STATUS_OK;
    inspection->header_size = header->header_size;
    inspection->payload_length = header->payload_length;
    inspection->payload_checksum = header->payload_checksum;
    inspection->sequence = header->sequence;
    inspection->correlation_id = header->correlation_id;
    memcpy(inspection->tag, header->tag, sizeof(inspection->tag));

    return 0;
}

TM_API int32_t TM_CALL tm_session_flush(tm_session* session, uint32_t timeout_ms)
{
    if (session == nullptr)
    {
        return TM_STATUS_BAD_ARGUMENT;
    }

    const DWORD wait_result = WaitForSingleObject(session->drained_event, timeout_ms == 0 ? INFINITE : timeout_ms);
    return wait_result == WAIT_OBJECT_0 ? 0 : TM_STATUS_INTERNAL_ERROR;
}
