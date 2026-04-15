# Memory Corruption Lab

Production-grade teaching demo for an expert C# systems-programming course. The scenario is a telemetry ingestion pipeline with:

- a native packet-processing session implemented as a C-style DLL on top of the Windows heap,
- a managed .NET console host that builds frames, crosses the interop boundary, registers an unmanaged callback, and drives both safe-ish and unsafe paths,
- two intentionally planted corruption bugs with different failure modes.

The design goal is not "write past the end and explode immediately". It is "production-adjacent code with plausible ABI, ownership, lifetime and delayed-failure problems that work well in WinDbg".

## Solution Layout

- `managed/`: .NET console host with `LibraryImport`, `SafeHandle`, `delegate* unmanaged`, `Span<T>`, `Unsafe`, `MemoryMarshal`, and multiple repro modes.
- `native/`: C++ DLL with C-style exports, Windows worker thread, unmanaged queue nodes, packet buffer allocation, checksum/inspection helpers, and the intentional bugs.
- `build.ps1`: convenience build entry point.
- `ARCHITECTURE.md`: component and ownership model.
- `WINDBG.md`: exact debugger walkthroughs and a reusable investigation algorithm.
- `LECTURE.md`: concise live-demo script.

## Build

Preferred:

```powershell
.\src\06.MemoryCorruptionLab\build.ps1
```

Equivalent direct build:

```powershell
dotnet build src\06.MemoryCorruptionLab\managed\Csharp14.SystemsMemory.MemoryCorruptionLab.csproj -c Debug
```

`managed` builds the native `TelemetryNative.vcxproj` automatically and copies `TelemetryNative.dll` and `TelemetryNative.pdb` next to the app host.

Visual Studio path:

1. Open `Csharp14.SystemsMemory.sln`.
2. Select `Debug | x64`.
3. Build the solution or just `06.MemoryCorruptionLab`.

## Run

Healthy copy path:

```powershell
dotnet run --project src\06.MemoryCorruptionLab\managed -- --mode healthy
```

Deterministic size/layout corruption:

```powershell
dotnet run --project src\06.MemoryCorruptionLab\managed -- --mode bugA
```

Delayed lifetime/ownership corruption:

```powershell
dotnet run --project src\06.MemoryCorruptionLab\managed -- --mode bugB
```

Zero-copy path fixed correctly:

```powershell
dotnet run --project src\06.MemoryCorruptionLab\managed -- --mode fixed
```

Optional knobs:

- `--delay 250`: increases native worker delay.
- `--sequence 123`: changes the frame sequence number.
- `--tag OPSDEMO`: changes the inline 8-byte tag.

## Modes And Expected Symptoms

- `healthy`: managed copy submit, correct lifetime, correct layout. Completion status is `0`.
- `bugA`: native copy path uses `sizeof(buggy_header_layout)` instead of the packed wire header size, so it copies 96 bytes into an 88-byte logical frame region and overwrites the trailing guard. Completion status is `4` (`TM_STATUS_TRAILER_CORRUPTED`).
- `bugB`: zero-copy submit stores only a raw pointer. Managed code disposes the owning native packet buffer immediately after enqueue. The release path poisons freed memory with `0xDD`, so the delayed worker usually reports `2` (`TM_STATUS_BAD_HEADER`). With full page heap, this mode often AVs in native code instead.
- `fixed`: same zero-copy fast path as `bugB`, but the buffer handle is retained until the completion callback fires.

## Feature Map

| Feature | Where it appears | Why it is there |
| --- | --- | --- |
| `stackalloc` | `managed/Telemetry/FrameCodec.cs` | Builds small sample payloads on the stack. |
| `Span<T>` / `ReadOnlySpan<T>` | `FrameCodec`, `TelemetryWireHeader` | Managed framing/parsing without heap churn. |
| `unsafe` pointers | `FrameCodec.WriteFrameFast`, `TelemetrySession`, callback code | Explicit low-level buffer access and interop. |
| `fixed` | `TelemetrySession.SubmitCopyAsync`, `TelemetryWireHeader.SetTag` | Short-lived pinning of managed data and fixed buffer access. |
| fixed-size buffer | `TelemetryWireHeader.Tag` | Inline 8-byte telemetry tag in the wire header. |
| `nint` / `nuint` | `FrameCodec.WriteFrameFast` | Native-sized offset arithmetic on raw pointers. |
| `where T : unmanaged` | `FrameCodec.WriteUnmanaged<T>` | Low-level serializer for blittable structs only. |
| function pointers | `CompletionRouter.Callback` + native `tm_session_register_callback` | Real async completion callback from the native worker thread. |
| `Unsafe` helpers | `Unsafe.WriteUnaligned`, `Unsafe.ReadUnaligned`, `Unsafe.AddByteOffset`, `Unsafe.AsRef` | Explicit byte-level wire manipulation. |
| `MemoryMarshal` | `MemoryMarshal.AsBytes`, `MemoryMarshal.GetReference` | Bridge between `Span<TelemetrySample>` and raw frame bytes. |
| `SafeHandle` | `TelemetrySessionHandle`, `NativePacketBufferHandle` | Correct ownership wrappers for native resources. |
| `LibraryImport` | `managed/Interop/NativeMethods.cs` | Explicit source-generated P/Invoke boundary. |

## Bug Map

| Bug | Root cause | Actual corruption site | Symptom site | Why the symptom misleads |
| --- | --- | --- | --- | --- |
| `bugA` | Size/layout mismatch: native code uses unpacked `tm_buggy_header_layout` (`48`) instead of packed wire header (`40`) | `tm_session_submit_copy` overwrites the trailing guard while copying into a heap queue node | Later, the worker detects a corrupted trailer and reports `TM_STATUS_TRAILER_CORRUPTED` | Students first stare at the worker or callback path, but the last plausible writer is earlier, during enqueue. |
| `bugB` | Lifetime/ownership violation: zero-copy submit keeps only a raw pointer, while managed code frees the owning native packet buffer immediately | `tm_packet_buffer_release` ends the pointer's lifetime; later worker reads/writes through a stale pointer | Default repro: worker reports `TM_STATUS_BAD_HEADER`. Under page heap: AV in native worker | The crash or bad-header report happens on the delayed processing thread, long after the original contract violation. |

## Why This Design

The hardest part to teach well is not syntax. It is the model:

- ABI decides what bytes mean.
- ownership decides who may free.
- lifetime decides how long a pointer remains usable.
- crash site and corruption site are often different.

`bugA` isolates arithmetic/layout reasoning. `bugB` isolates provenance, ownership, delayed access and stale pointers. Together they support a reusable investigation workflow instead of a single one-off trick.

## Reliability Tradeoff

For classroom reliability, `bugB` defaults to a deterministic poisoned-free symptom (`bad header`) instead of relying purely on allocator luck. On a machine with full page heap enabled for the app host, the same stale-pointer bug commonly turns into a cleaner AV, which is ideal for WinDbg.
