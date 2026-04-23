#define WIN32_LEAN_AND_MEAN

#include "HeapBufferNative.h"

#include <windows.h>
#include <memory.h>

namespace
{
    constexpr uint32_t HB_STATUS_OK = 0;
    constexpr uint32_t HB_STATUS_BAD_ARGUMENT = 1;
    constexpr uint32_t HB_STATUS_INTERNAL_ERROR = 2;

    constexpr uint32_t HB_BLOCK_MAGIC = 0x31424648u;

    struct hb_block_header
    {
        uint32_t magic;
        uint32_t frame_capacity;
        uint32_t allocation_size;
        uint32_t reserved;
    };

    uint8_t* hb_data(hb_block_header* block)
    {
        return reinterpret_cast<uint8_t*>(block + 1);
    }

    bool hb_valid(hb_block_header* block)
    {
        return block != nullptr && block->magic == HB_BLOCK_MAGIC;
    }
}

struct hb_block
{
};

HB_API int32_t HB_CALL hb_alloc(uint32_t frame_capacity, hb_block** out_block)
{
    if (out_block == nullptr || frame_capacity == 0)
    {
        return HB_STATUS_BAD_ARGUMENT;
    }

    const SIZE_T allocation_size = sizeof(hb_block_header) + frame_capacity;

    auto* block = static_cast<hb_block_header*>(
        HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, allocation_size));

    if (block == nullptr)
    {
        return HB_STATUS_INTERNAL_ERROR;
    }

    block->magic = HB_BLOCK_MAGIC;
    block->frame_capacity = frame_capacity;
    block->allocation_size = frame_capacity;

    *out_block = reinterpret_cast<hb_block*>(block);
    return HB_STATUS_OK;
}

HB_API void HB_CALL hb_free(hb_block* raw_block)
{
    auto* block = reinterpret_cast<hb_block_header*>(raw_block);
    if (!hb_valid(block))
    {
        return;
    }

    const SIZE_T bytes = sizeof(hb_block_header) + block->allocation_size;
    memset(block, 0xDD, bytes);
    HeapFree(GetProcessHeap(), 0, block);
}

HB_API void* HB_CALL hb_frame_data(hb_block* raw_block)
{
    auto* block = reinterpret_cast<hb_block_header*>(raw_block);
    return hb_valid(block) ? hb_data(block) : nullptr;
}

HB_API uint32_t HB_CALL hb_frame_capacity(hb_block* raw_block)
{
    auto* block = reinterpret_cast<hb_block_header*>(raw_block);
    return hb_valid(block) ? block->frame_capacity : 0u;
}

HB_API uint32_t HB_CALL hb_allocation_size(hb_block* raw_block)
{
    auto* block = reinterpret_cast<hb_block_header*>(raw_block);
    return hb_valid(block) ? block->allocation_size : 0u;
}
