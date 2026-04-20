#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "CompactTelemetryNative.h"

#include <intrin.h>
#include <memory.h>

namespace
{
    constexpr uint32_t CT_STATUS_OK = 0;
    constexpr uint32_t CT_STATUS_BAD_ARGUMENT = 1;
    constexpr uint32_t CT_STATUS_BAD_HEADER = 2;
    constexpr uint32_t CT_STATUS_TRAILER_CORRUPTED = 4;
    constexpr uint32_t CT_STATUS_INTERNAL_ERROR = 5;

    constexpr uint32_t CT_WIRE_MAGIC = 0x314D4C54u;
    constexpr uint32_t CT_BUFFER_MAGIC = 0x52464642u;
    constexpr uint64_t CT_TRAILER_COOKIE = 0x544D475541524431ull;
    constexpr uint32_t CT_WORKER_DELAY_MS = 150;

#pragma pack(push, 1)
    struct ct_wire_header
    {
        uint32_t magic;
        uint16_t header_size;
        uint16_t opcode;
        uint32_t payload_length;
        uint32_t sequence;
        uint16_t flags;
        uint16_t reserved;
        uint64_t correlation_id;
        uint8_t tag[8];
        uint32_t payload_checksum;
    };
#pragma pack(pop)

    struct ct_trailer
    {
        uint64_t cookie;
    };

    static_assert(sizeof(ct_wire_header) == 40, "Packed wire header must stay at 40 bytes.");
}

struct ct_buffer
{
    uint32_t magic;
    uint32_t capacity;
    uint8_t* data;
};

struct ct_pending_frame
{
    ct_pending_frame* next;
    uint8_t* frame;
    uint32_t frame_length;
    uint32_t copied_length;
    ct_trailer* trailer;
};

struct ct_session
{
    CRITICAL_SECTION lock;
    HANDLE work_event;
    HANDLE drained_event;
    HANDLE worker_thread;
    volatile LONG stop_requested;
    volatile LONG in_flight;
    ct_pending_frame* head;
    ct_pending_frame* tail;
    ct_completion_callback callback;
    void* callback_context;
};

namespace
{
    uint32_t ct_compute_checksum(const uint8_t* data, uint32_t length)
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

    bool ct_validate_frame(uint8_t* frame, uint32_t frame_length, uint32_t* observed_checksum)
    {
        if (frame == nullptr || frame_length < sizeof(ct_wire_header))
        {
            return false;
        }

        auto* header = reinterpret_cast<ct_wire_header*>(frame);
        if (header->magic != CT_WIRE_MAGIC || header->header_size != sizeof(ct_wire_header))
        {
            return false;
        }

        if (header->payload_length > frame_length - header->header_size)
        {
            return false;
        }

        uint8_t* payload = frame + header->header_size;
        *observed_checksum = ct_compute_checksum(payload, header->payload_length);
        return *observed_checksum == header->payload_checksum;
    }

    bool ct_queue_empty(ct_session* session)
    {
        EnterCriticalSection(&session->lock);
        bool empty = session->head == nullptr;
        LeaveCriticalSection(&session->lock);
        return empty;
    }

    void ct_enqueue(ct_session* session, ct_pending_frame* pending)
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

    ct_pending_frame* ct_try_dequeue(ct_session* session)
    {
        EnterCriticalSection(&session->lock);

        ct_pending_frame* pending = session->head;
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

    void ct_complete(ct_session* session, ct_pending_frame* pending, const ct_completion& completion)
    {
        if (session->callback != nullptr)
        {
            session->callback(session->callback_context, &completion);
        }

        HeapFree(GetProcessHeap(), 0, pending);

        const LONG remaining = InterlockedDecrement(&session->in_flight);
        if (remaining == 0 && ct_queue_empty(session))
        {
            SetEvent(session->drained_event);
        }
    }

    void ct_process_pending(ct_session* session, ct_pending_frame* pending)
    {
        Sleep(CT_WORKER_DELAY_MS);

        ct_completion completion = {};
        completion.submitted_length = pending->frame_length;
        completion.copied_length = pending->copied_length;
        completion.frame_address = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(pending->frame));

        if (pending->trailer != nullptr && pending->trailer->cookie != CT_TRAILER_COOKIE)
        {
            completion.status = CT_STATUS_TRAILER_CORRUPTED;
            ct_complete(session, pending, completion);
            return;
        }

        uint32_t observed_checksum = 0;
        if (!ct_validate_frame(pending->frame, pending->frame_length, &observed_checksum))
        {
            completion.status = CT_STATUS_BAD_HEADER;
            ct_complete(session, pending, completion);
            return;
        }

        completion.status = CT_STATUS_OK;
        completion.observed_checksum = observed_checksum;

        auto* header = reinterpret_cast<ct_wire_header*>(pending->frame);
        header->flags |= 0x8000u;

        ct_complete(session, pending, completion);
    }

    DWORD WINAPI ct_worker_entry(void* parameter)
    {
        auto* session = static_cast<ct_session*>(parameter);

        for (;;)
        {
            if (WaitForSingleObject(session->work_event, INFINITE) != WAIT_OBJECT_0)
            {
                return 1;
            }

            for (;;)
            {
                ct_pending_frame* pending = ct_try_dequeue(session);
                if (pending == nullptr)
                {
                    break;
                }

                ct_process_pending(session, pending);
            }

            if (session->stop_requested != 0 && ct_queue_empty(session))
            {
                return 0;
            }
        }
    }
}

CT_API int32_t CT_CALL ct_session_open(ct_session** out_session)
{
    if (out_session == nullptr)
    {
        return CT_STATUS_BAD_ARGUMENT;
    }

    auto* session = static_cast<ct_session*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(ct_session)));
    if (session == nullptr)
    {
        return CT_STATUS_INTERNAL_ERROR;
    }

    InitializeCriticalSection(&session->lock);
    session->work_event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    session->drained_event = CreateEventW(nullptr, TRUE, TRUE, nullptr);
    session->worker_thread = CreateThread(nullptr, 0, ct_worker_entry, session, 0, nullptr);

    if (session->work_event == nullptr || session->drained_event == nullptr || session->worker_thread == nullptr)
    {
        if (session->worker_thread != nullptr)
        {
            CloseHandle(session->worker_thread);
        }

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
        return CT_STATUS_INTERNAL_ERROR;
    }

    *out_session = session;
    return CT_STATUS_OK;
}

CT_API void CT_CALL ct_session_close(ct_session* session)
{
    if (session == nullptr)
    {
        return;
    }

    session->stop_requested = 1;
    SetEvent(session->work_event);
    WaitForSingleObject(session->worker_thread, INFINITE);

    ct_pending_frame* pending = nullptr;
    while ((pending = ct_try_dequeue(session)) != nullptr)
    {
        HeapFree(GetProcessHeap(), 0, pending);
    }

    CloseHandle(session->worker_thread);
    CloseHandle(session->work_event);
    CloseHandle(session->drained_event);
    DeleteCriticalSection(&session->lock);
    HeapFree(GetProcessHeap(), 0, session);
}

CT_API int32_t CT_CALL ct_session_register_callback(ct_session* session, ct_completion_callback callback, void* context)
{
    if (session == nullptr || callback == nullptr)
    {
        return CT_STATUS_BAD_ARGUMENT;
    }

    session->callback = callback;
    session->callback_context = context;
    return CT_STATUS_OK;
}

CT_API int32_t CT_CALL ct_session_submit_copy(ct_session* session, const void* frame, uint32_t frame_length)
{
    if (session == nullptr || frame == nullptr || frame_length < sizeof(ct_wire_header))
    {
        return CT_STATUS_BAD_ARGUMENT;
    }

    const SIZE_T allocation_size = sizeof(ct_pending_frame) + frame_length + sizeof(ct_trailer);
    auto* pending = static_cast<ct_pending_frame*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, allocation_size));
    if (pending == nullptr)
    {
        return CT_STATUS_INTERNAL_ERROR;
    }

    pending->frame = reinterpret_cast<uint8_t*>(pending + 1);
    pending->frame_length = frame_length;
    pending->copied_length = frame_length;
    pending->trailer = reinterpret_cast<ct_trailer*>(pending->frame + frame_length);
    pending->trailer->cookie = CT_TRAILER_COOKIE;

    pending->copied_length = frame_length;
    memcpy(pending->frame, frame, frame_length);
    ct_enqueue(session, pending);
    return CT_STATUS_OK;
}

CT_API int32_t CT_CALL ct_session_submit_zero_copy(ct_session* session, void* frame, uint32_t frame_length)
{
    if (session == nullptr || frame == nullptr || frame_length < sizeof(ct_wire_header))
    {
        return CT_STATUS_BAD_ARGUMENT;
    }

    auto* pending = static_cast<ct_pending_frame*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(ct_pending_frame)));
    if (pending == nullptr)
    {
        return CT_STATUS_INTERNAL_ERROR;
    }

    pending->frame = static_cast<uint8_t*>(frame);
    pending->frame_length = frame_length;
    pending->copied_length = frame_length;
    pending->trailer = nullptr;

    ct_enqueue(session, pending);
    return CT_STATUS_OK;
}

CT_API int32_t CT_CALL ct_session_flush(ct_session* session, uint32_t timeout_ms)
{
    if (session == nullptr)
    {
        return CT_STATUS_BAD_ARGUMENT;
    }

    DWORD timeout = timeout_ms == 0 ? INFINITE : timeout_ms;
    return WaitForSingleObject(session->drained_event, timeout) == WAIT_OBJECT_0 ? CT_STATUS_OK : CT_STATUS_INTERNAL_ERROR;
}

CT_API int32_t CT_CALL ct_buffer_alloc(uint32_t capacity, ct_buffer** out_buffer)
{
    if (out_buffer == nullptr || capacity < sizeof(ct_wire_header))
    {
        return CT_STATUS_BAD_ARGUMENT;
    }

    auto* buffer = static_cast<ct_buffer*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(ct_buffer) + capacity));
    if (buffer == nullptr)
    {
        return CT_STATUS_INTERNAL_ERROR;
    }

    buffer->magic = CT_BUFFER_MAGIC;
    buffer->capacity = capacity;
    buffer->data = reinterpret_cast<uint8_t*>(buffer + 1);

    *out_buffer = buffer;
    return CT_STATUS_OK;
}

CT_API void* CT_CALL ct_buffer_data(ct_buffer* buffer)
{
    if (buffer == nullptr || buffer->magic != CT_BUFFER_MAGIC)
    {
        return nullptr;
    }

    return buffer->data;
}

CT_API uint32_t CT_CALL ct_buffer_capacity(ct_buffer* buffer)
{
    if (buffer == nullptr || buffer->magic != CT_BUFFER_MAGIC)
    {
        return 0;
    }

    return buffer->capacity;
}

CT_API void CT_CALL ct_buffer_free(ct_buffer* buffer)
{
    if (buffer == nullptr)
    {
        return;
    }

    const uint32_t capacity = buffer->capacity;
    memset(buffer, 0xDD, sizeof(ct_buffer) + capacity);
    HeapFree(GetProcessHeap(), 0, buffer);
}
