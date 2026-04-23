# Module 03 Lecture Flow

This branch has one terminal scenario: unmanaged heap corruption caused by managed unsafe pointer arithmetic.

The native side is infrastructure, not the source of the bug. It allocates a Windows heap block, exposes a frame pointer and capacity, and releases the block. The corrupting write remains in managed code.

## Story

Managed code asks native for a heap buffer large enough to hold one telemetry frame.

Managed code then writes a valid frame into that buffer:

- packed 40-byte header,
- 48-byte payload,
- 88 bytes total.

After the frame is built, managed code performs one more ordinary-looking step: `CommitTransportMetadata()`. That step writes a small footer-like metadata record next to the frame.

The bug is that the buffer was allocated for the frame only. No metadata area was reserved after it.

So the frame is valid, but the later metadata write crosses the heap allocation boundary. With full page heap enabled, WinDbg should stop close to that invalid write. Without page heap, the process may survive the write and fail when the corrupted heap block is released.

## Files To Show

1. [TelemetryWireHeader.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Telemetry/TelemetryWireHeader.cs)
2. [TelemetrySample.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Telemetry/TelemetrySample.cs)
3. [FrameCodec.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Telemetry/FrameCodec.cs)
4. [NativeMethods.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Interop/NativeMethods.cs)
5. [HeapBuffer.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Interop/HeapBuffer.cs)
6. [TelemetryFrameTransport.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/Interop/TelemetryFrameTransport.cs)
7. [DemoRunner.cs](c:/git/csharp14-systems-memory/src/03.Unsafe/DemoRunner.cs)

You do not need to open the native implementation during the first pass. Treat it as the heap service: allocate, expose pointer/capacity, release.

## First Run

```powershell
dotnet run --project src\03.Unsafe
```

Expected shape:

- the frame prints as valid,
- frame length is 88,
- allocation size is 88,
- metadata commit returns,
- the process may then terminate with heap corruption when the buffer is released.

The important observation is the size mismatch in the contract:

```text
frame length    : 88
allocation size : 88
```

There is no room after the frame.

## Investigation

Go to `TelemetryFrameTransport.CommitTransportMetadata()`.

The method writes two normal-looking fields:

- metadata magic,
- frame length.

Then inspect `GetTransportMetadataSpan()`.

The metadata offset is computed from the aligned frame length:

```csharp
int metadataOffset = AlignUp(FrameLength, TransportMetadataSize);
```

For this frame, `FrameLength` is 88, and 88 is already aligned to 8. So the metadata span begins at byte 88.

But the allocation is exactly 88 bytes long. Valid frame bytes are `[0..87]`. Byte 88 is already outside the allocated frame buffer.

That is the managed-side bug: the code derives a second write location that the allocation contract never gave it.

## WinDbg Run

Build first:

```powershell
dotnet build src\03.Unsafe
```

Enable full page heap for the apphost exe:

```powershell
gflags /p /enable Csharp14.SystemsMemory.Unsafe.exe /full
```

Run under WinDbg:

```powershell
windbg src\03.Unsafe\bin\Debug\net10.0\Csharp14.SystemsMemory.Unsafe.exe
```

With full page heap, the invalid metadata write should be caught close to the write site instead of being discovered later during heap release.

After the demo, disable page heap:

```powershell
gflags /p /disable Csharp14.SystemsMemory.Unsafe.exe
```

## Fix Direction

The fix is to make the memory contract honest:

- reserve metadata space in the native allocation,
- or store metadata inside the frame format,
- or reject `CommitTransportMetadata()` when there is no capacity after the frame.

Do not "fix" the symptom by changing the heap release path. The bad write is the bug.
