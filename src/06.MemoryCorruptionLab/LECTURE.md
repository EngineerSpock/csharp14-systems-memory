# Lecture Script

## 1. Start Healthy

Run:

```powershell
dotnet run --project src\06.MemoryCorruptionLab\managed -- --mode healthy
```

Show in code:

- `FrameCodec.BuildManagedFrame`
- `TelemetryWireHeader`
- `TelemetrySession.SubmitCopyAsync`
- `tm_session_submit_copy`
- native worker callback registration

Message to students:

- the system already mixes managed spans, unsafe code, callbacks, native ownership and a worker thread,
- nothing is wrong yet,
- this is the baseline mental model.

## 2. Trigger Bug A

Run:

```powershell
dotnet run --project src\06.MemoryCorruptionLab\managed -- --mode bugA
```

Ask for first hypotheses:

- "The worker parser is wrong."
- "Managed serialized the header incorrectly."
- "Checksum code is wrong."

Guide them:

1. Compare `submitted length` and `copied length`.
2. Show packed managed header size is `40`.
3. Show native buggy header size is `48`.
4. Emphasize that the symptom happens in the worker, but the write happened during enqueue.

Teaching point:

- this is the "wrong bytes at the right address" class.

## 3. Trigger Bug B

Run:

```powershell
dotnet run --project src\06.MemoryCorruptionLab\managed -- --mode bugB
```

Show in code:

- `FrameCodec.WriteFrameFast`
- `TelemetrySession.SubmitZeroCopyAsync`
- immediate `buffer.Dispose()`
- native zero-copy path stores only `void* frame`

Likely student hypotheses:

- "The native parser is randomly flaky."
- "The checksum logic is nondeterministic."
- "The callback path is broken."

Guide them:

1. The frame was valid at inspection time before submit.
2. The worker fails later on a different thread.
3. The owner was released before native processing completed.
4. Therefore the pointer provenance is correct but the lifetime contract is broken.

Teaching point:

- this is the "address used after lifetime ended" class.

## 4. Show The Fix

Run:

```powershell
dotnet run --project src\06.MemoryCorruptionLab\managed -- --mode fixed
```

Show:

- `CompletionRouter` retaining `NativePacketBufferHandle`
- release only after callback

Closing message:

- the fix is not "sprinkle more unsafe carefully",
- the fix is "state the ownership contract and encode it in code".
