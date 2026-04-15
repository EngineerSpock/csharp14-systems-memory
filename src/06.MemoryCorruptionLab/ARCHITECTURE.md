# Architecture

## Scenario

The demo models a telemetry ingestion session:

1. Managed code builds a packed binary frame.
2. The frame crosses into a native session.
3. The native session either copies the frame into a queue node or enqueues a raw pointer for zero-copy processing.
4. A native worker thread parses the header, checks payload integrity, mutates the frame in place to mark it processed, and raises a completion callback back into managed code.

This gives one coherent reason to discuss:

- layout-sensitive headers,
- stack vs heap storage,
- short-lived pins,
- native ownership,
- worker-thread delays,
- callback context lifetime,
- stale pointers that survive the call boundary.

## Wire Format

Packed header, `40` bytes total:

| Offset | Size | Field |
| --- | --- | --- |
| `0` | `4` | `Magic` |
| `4` | `2` | `HeaderSize` |
| `6` | `2` | `Opcode` |
| `8` | `4` | `PayloadLength` |
| `12` | `4` | `Sequence` |
| `16` | `2` | `Flags` |
| `18` | `2` | `Reserved` |
| `20` | `8` | `CorrelationId` |
| `28` | `8` | `Tag[8]` |
| `36` | `4` | `PayloadChecksum` |

Payload:

- three `TelemetrySample` records,
- each record is blittable and layout-dependent,
- total payload length in the default demo is `48` bytes.

Total frame length in the default repro is `88` bytes.

## Managed Side

### `FrameCodec`

- `BuildManagedFrame`: safe-ish framing path over `Span<byte>`.
- `WriteFrameFast`: zero-copy unsafe path that writes directly into native memory.
- `DescribeManagedFrame`: quick managed-side parse to contrast with native inspection.

Important teaching points:

- payload is produced from a stack-allocated span of unmanaged structs,
- `MemoryMarshal.AsBytes` bridges structured data to the wire,
- `Unsafe.WriteUnaligned` and `Unsafe.AddByteOffset` make byte-based layout intent explicit,
- the payload start is computed in bytes, not in elements.

### `TelemetrySession`

Managed wrapper around the native session:

- owns the native session through `TelemetrySessionHandle`,
- registers an unmanaged callback through `CompletionRouter`,
- exposes copy and zero-copy submit paths,
- allocates native packet buffers wrapped by `NativePacketBufferHandle`.

### `CompletionRouter`

The callback context is a `GCHandle` to a managed router object. That router:

- matches completion tokens to `TaskCompletionSource`,
- optionally retains zero-copy buffer handles until completion,
- provides the `delegate* unmanaged[Cdecl]` callback pointer.

This is intentionally explicit ownership code, not hidden behind a framework.

## Native Side

### Exports

- `tm_session_open` / `tm_session_close`
- `tm_session_register_callback`
- `tm_session_submit_copy`
- `tm_session_submit_zero_copy`
- `tm_session_allocate_packet_buffer`
- `tm_packet_buffer_data`
- `tm_packet_buffer_capacity`
- `tm_packet_buffer_release`
- `tm_compute_checksum`
- `tm_inspect_frame`
- `tm_session_flush`

### Session internals

Each session owns:

- a Windows worker thread,
- a work event and a drained event,
- a linked-list queue of pending frames on the unmanaged heap,
- callback function pointer plus opaque callback context.

### Copy path

`tm_session_submit_copy` allocates:

- a pending-frame node,
- copied frame bytes,
- a debug trailer with a canary.

That trailer is the deterministic victim for `bugA`.

### Zero-copy path

`tm_session_submit_zero_copy` stores only:

- `void* frame`,
- frame length,
- token.

It does not own the backing storage. That is intentional. The lifetime contract is therefore external and visible.

### Packet buffer resource

`tm_session_allocate_packet_buffer` returns a separate native resource used for zero-copy framing.

The caller owns it. The session does not.

That resource exists so the fixed solution can teach the rule:

- "retain the owner until the callback says the native side is done".

## Ownership Model

### Healthy copy path

- managed owns `byte[]`,
- `fixed` pins the array only for the call,
- native copies bytes immediately,
- after return the managed buffer may move freely,
- queue node owns the copied bytes.

### Bug A

- ownership is correct,
- lifetime is correct,
- bytes are wrong because the native side computes size from the wrong struct layout.

### Bug B

- native queue stores only a raw frame pointer,
- managed code frees the owning `NativePacketBufferHandle` too early,
- worker later dereferences a stale pointer,
- the releaser poisons freed memory with `0xDD` before `HeapFree` so the stale read is easy to observe,
- under full page heap this often becomes an AV instead.

### Fixed zero-copy path

- zero-copy is still used,
- but `CompletionRouter` retains the `NativePacketBufferHandle`,
- the handle is disposed only after the native callback arrives.

## Why The Two Bugs Are Different

`bugA` is "wrong bytes at the right address":

- same ownership,
- same lifetime,
- wrong size arithmetic and wrong ABI assumption.

`bugB` is "address used after lifetime ended":

- pointer provenance is real,
- bytes may once have been correct,
- the address is stale because the owner was released too soon.
