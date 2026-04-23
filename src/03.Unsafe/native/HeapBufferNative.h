#pragma once

#include <stdint.h>

#ifdef HEAP_BUFFER_NATIVE_EXPORTS
#define HB_API extern "C" __declspec(dllexport)
#else
#define HB_API extern "C" __declspec(dllimport)
#endif

#define HB_CALL __cdecl

struct hb_block;

HB_API int32_t HB_CALL hb_alloc(uint32_t frame_capacity, hb_block** out_block);
HB_API void HB_CALL hb_free(hb_block* block);
HB_API void* HB_CALL hb_frame_data(hb_block* block);
HB_API uint32_t HB_CALL hb_frame_capacity(hb_block* block);
HB_API uint32_t HB_CALL hb_allocation_size(hb_block* block);
