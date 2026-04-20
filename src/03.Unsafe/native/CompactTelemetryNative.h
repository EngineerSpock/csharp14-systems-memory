#pragma once

#include <stdint.h>

#ifdef COMPACTTELEMETRYNATIVE_EXPORTS
#define CT_API extern "C" __declspec(dllexport)
#else
#define CT_API extern "C" __declspec(dllimport)
#endif

#define CT_CALL __cdecl

struct ct_session;
struct ct_buffer;

struct ct_completion
{
    uint32_t status;
    uint32_t submitted_length;
    uint32_t copied_length;
    uint32_t observed_checksum;
    uint64_t frame_address;
};

typedef void (CT_CALL* ct_completion_callback)(void* context, const ct_completion* completion);

CT_API int32_t CT_CALL ct_session_open(ct_session** out_session);
CT_API void CT_CALL ct_session_close(ct_session* session);
CT_API int32_t CT_CALL ct_session_register_callback(ct_session* session, ct_completion_callback callback, void* context);
CT_API int32_t CT_CALL ct_session_submit_copy(ct_session* session, const void* frame, uint32_t frame_length);
CT_API int32_t CT_CALL ct_session_submit_zero_copy(ct_session* session, void* frame, uint32_t frame_length);
CT_API int32_t CT_CALL ct_session_flush(ct_session* session, uint32_t timeout_ms);

CT_API int32_t CT_CALL ct_buffer_alloc(uint32_t capacity, ct_buffer** out_buffer);
CT_API void* CT_CALL ct_buffer_data(ct_buffer* buffer);
CT_API uint32_t CT_CALL ct_buffer_capacity(ct_buffer* buffer);
CT_API void CT_CALL ct_buffer_free(ct_buffer* buffer);
