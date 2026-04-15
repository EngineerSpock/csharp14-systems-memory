#pragma once

#include <stdint.h>

#ifdef TELEMETRYNATIVE_EXPORTS
#define TM_API extern "C" __declspec(dllexport)
#else
#define TM_API extern "C" __declspec(dllimport)
#endif

#define TM_CALL __cdecl

enum tm_demo_mode : uint32_t
{
    TM_DEMO_HEALTHY = 0,
    TM_DEMO_LAYOUT_OVERFLOW = 1
};

enum tm_completion_status : uint32_t
{
    TM_STATUS_OK = 0,
    TM_STATUS_BAD_ARGUMENT = 1,
    TM_STATUS_BAD_HEADER = 2,
    TM_STATUS_CHECKSUM_MISMATCH = 3,
    TM_STATUS_TRAILER_CORRUPTED = 4,
    TM_STATUS_INTERNAL_ERROR = 5
};

struct tm_session;
struct tm_packet_buffer;

typedef void (TM_CALL* tm_completion_callback)(void* context, const struct tm_completion* completion);

#pragma pack(push, 1)
struct tm_wire_header
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

static_assert(sizeof(tm_wire_header) == 40, "Wire header size must stay stable for the demo.");

struct tm_buggy_header_layout
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

static_assert(sizeof(tm_buggy_header_layout) == 48, "Buggy header layout must differ from the wire layout.");

struct tm_session_options
{
    uint32_t struct_size;
    uint32_t demo_mode;
    uint32_t worker_delay_ms;
    uint32_t debug_flags;
};

struct tm_completion
{
    uint64_t token;
    uint32_t status;
    uint32_t submitted_length;
    uint32_t observed_checksum;
    uint32_t copied_length;
    uint32_t reserved;
    uint64_t frame_address;
    uint64_t note1;
    uint64_t note2;
};

struct tm_packet_inspection
{
    uint32_t status;
    uint32_t header_size;
    uint32_t payload_length;
    uint32_t payload_checksum;
    uint32_t sequence;
    uint64_t correlation_id;
    uint8_t tag[8];
};

TM_API int32_t TM_CALL tm_session_open(const tm_session_options* options, tm_session** out_session);
TM_API void TM_CALL tm_session_close(tm_session* session);
TM_API int32_t TM_CALL tm_session_register_callback(tm_session* session, tm_completion_callback callback, void* context);
TM_API int32_t TM_CALL tm_session_submit_copy(tm_session* session, const void* frame, uint32_t frame_length, uint64_t token);
TM_API int32_t TM_CALL tm_session_submit_zero_copy(tm_session* session, void* frame, uint32_t frame_length, uint64_t token);
TM_API int32_t TM_CALL tm_session_allocate_packet_buffer(tm_session* session, uint32_t capacity, tm_packet_buffer** out_buffer);
TM_API void* TM_CALL tm_packet_buffer_data(tm_packet_buffer* buffer);
TM_API uint32_t TM_CALL tm_packet_buffer_capacity(const tm_packet_buffer* buffer);
TM_API void TM_CALL tm_packet_buffer_release(tm_packet_buffer* buffer);
TM_API uint32_t TM_CALL tm_compute_checksum(const void* data, uint32_t length);
TM_API int32_t TM_CALL tm_inspect_frame(const void* frame, uint32_t frame_length, tm_packet_inspection* inspection);
TM_API int32_t TM_CALL tm_session_flush(tm_session* session, uint32_t timeout_ms);
