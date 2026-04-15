# WinDbg Walkthrough

The demo is Windows-first and x64-only on purpose. Build `Debug | x64` so both managed and native PDBs are available.

## Build And Symbols

Build:

```powershell
dotnet build src\06.MemoryCorruptionLab\managed\Csharp14.SystemsMemory.MemoryCorruptionLab.csproj -c Debug
```

App host:

```text
src\06.MemoryCorruptionLab\managed\bin\Debug\net10.0\Csharp14.SystemsMemory.MemoryCorruptionLab.exe
```

Suggested symbol path inside WinDbg:

```text
.symfix
.sympath+ srv*C:\symbols*https://msdl.microsoft.com/download/symbols
.reload /f
```

Why:

- `.symfix` gives you Microsoft public symbols quickly.
- `.sympath+` preserves the local demo PDBs and adds a local cache.
- `.reload /f` forces module reload so `TelemetryNative.pdb` and the managed PDB load before you start stepping around.

## Optional: Enable Full Page Heap

Recommended especially for `bugB`.

Enable:

```powershell
gflags /p /enable Csharp14.SystemsMemory.MemoryCorruptionLab.exe /full
```

Disable later:

```powershell
gflags /p /disable Csharp14.SystemsMemory.MemoryCorruptionLab.exe
```

Why:

- `bugA` becomes easier to reason about when heap diagnostics are active.
- `bugB` often turns from a late "bad header" into a clean AV on the stale pointer access.

## Bug A: Layout/Size Corruption

### Repro

Run under WinDbg:

```text
Csharp14.SystemsMemory.MemoryCorruptionLab.exe --mode bugA
```

The default debugger-friendly behavior is:

- enqueue happens successfully,
- the worker later detects the corrupted trailer,
- if a debugger is attached, native code executes `DebugBreak()` exactly when the guard check fails.

### First Commands

```text
g
k
!analyze -v
```

Why:

- `g` gets past the initial loader break.
- `k` shows that you stopped in the worker path, not in the original submit call.
- `!analyze -v` gives you fast context, but for this bug the important lesson is that the break site is a detector, not the writer.

### Inspect The Frame And Guard

From the breakpoint in native code, inspect locals if available:

```text
dv /t
```

You want the `pending` pointer. Then:

```text
dq <pending> L6
```

The node layout shows:

- frame pointer,
- logical frame length,
- copied length,
- token,
- trailer pointer.

Then inspect the frame bytes and the trailer:

```text
db <frame_pointer> L60
dq <trailer_pointer> L2
```

What to expect:

- logical length is `88`,
- copied length is `96`,
- trailer cookie is no longer `0x544D475541524431`,
- the corrupted cookie proves a write ran past the logical frame boundary.

### Find The Earlier Writer

Restart and break on the enqueue/export:

```text
bp TelemetryNative!tm_session_submit_copy
g
```

At the breakpoint:

```text
r rcx rdx r8 r9
db @rdx L60
```

Interpretation on x64 Windows:

- `rcx`: session,
- `rdx`: source frame pointer,
- `r8`: frame length,
- `r9`: token.

The input length is still `88`. The corruption has not happened yet.

Step over the export:

```text
gu
```

Then continue until the worker-side break again:

```text
g
```

This is the key reasoning jump:

- correct input bytes crossed the boundary,
- later the guard is bad,
- therefore the bug sits inside the native enqueue/copy logic,
- not in the managed serializer and not in the worker parser.

## Bug B: Delayed Stale Pointer / Ownership Violation

### Repro

Default deterministic repro:

```text
Csharp14.SystemsMemory.MemoryCorruptionLab.exe --mode bugB
```

What happens:

- zero-copy submit gives native code only a raw frame pointer,
- managed code immediately disposes the owning native packet buffer,
- release poisons freed memory with `0xDD`,
- later the worker thread touches a stale pointer,
- without page heap you usually stop on the native `DebugBreak()` when `tm_inspect_frame` sees an impossible header,
- with full page heap you often AV directly on the stale access.

### If You Hit An AV

Use:

```text
!analyze -v
.exr -1
k
r
```

Why:

- `!analyze -v` classifies the exception and often points to the native worker thread.
- `.exr -1` shows the last exception record, which is what you want for a real stale-pointer fault.
- `k` shows that the failure is delayed and happens off the original managed submit call.
- `r` gives you the actual stale address currently being dereferenced.

If the fault is in `tm_inspect_frame`, the stale frame pointer is usually in `rcx`. Inspect it:

```text
db @rcx L40
!heap -p -a @rcx
```

What to look for:

- `db` often shows `dd dd dd dd ...` because the release path poisoned the memory,
- `!heap -p -a` tells you whether the address belongs to a freed or page-heap-instrumented block.

### If You Stop On The Debug Break Instead

Use:

```text
k
dv /t
```

You want the `pending->frame` pointer. Then:

```text
db <pending->frame> L40
!heap -p -a <pending->frame>
```

Reasoning:

- the native parser is now reading nonsense bytes,
- the frame looked valid at submit time,
- therefore the pointer was once good but became invalid later,
- that points to lifetime/ownership, not to layout arithmetic.

## Managed Visibility Commands

These are especially useful when comparing `bugB` to `fixed`.

```text
!dumpheap -stat
!dumpheap -type NativePacketBufferHandle
!gcroot <managed_object_address>
!clrstack
```

Why:

- `!dumpheap -stat` tells you whether the process still has the managed objects you think it has.
- `!dumpheap -type NativePacketBufferHandle` helps you find the SafeHandle wrappers.
- `!gcroot` answers the crucial ownership question: "what is still keeping this managed object alive?"
- `!clrstack` shows whether the current thread is in managed code at all. On the crashing native worker thread it is usually empty or uninteresting, which is itself a clue.

Suggested comparison:

1. Run `fixed`.
2. Break after submit but before callback.
3. Find a `NativePacketBufferHandle`.
4. Run `!gcroot` on it.
5. Observe that `CompletionRouter._retainedBuffers` is the root that keeps the owner alive.

That root is exactly what is missing in `bugB`.

## Universal Investigation Algorithm

Use this on demos and on real incidents.

1. Classify the symptom.
   Immediate AV, delayed AV, heap corruption, crash on free, silent data corruption, bad callback/context, or unexplained failure after a managed/native crossing.

2. Decide whether the crash site is likely the corruption site.
   A guard failure, bad free, or parser complaint is often a detector, not the writer.

3. Establish pointer provenance.
   Ask whether the address came from stack memory, a pinned managed object, unmanaged heap, static native memory, or a block that may already be freed.

4. Check ABI assumptions.
   Verify struct layout, packing, field offsets, alignment, endianness, and whether the code mixed byte stepping with element stepping incorrectly.

5. Check ownership.
   Who allocates, who frees, who is allowed to retain the pointer, and whether both sides think they own the same memory.

6. Check lifetime.
   Was the pointer derived from `stackalloc`, a `fixed` scope, a temporary callback context, a SafeHandle that was disposed, or a buffer retained by a worker thread after the caller returned?

7. Verify the last plausible writer, not just the crashing reader.
   Corruption often becomes visible in a validator, parser, or free path. Walk backward to the last code that could have written the observed bytes.

8. Distinguish three failure classes explicitly.
   `wrong bytes at the right address`
   `right bytes at the wrong address`
   `address used after lifetime ended`

9. Use debugger evidence to choose the next branch.
   `!heap -p -a` answers provenance questions. `db/dd/dq` answer content questions. `!gcroot` answers managed retention questions. `k` and `!clrstack` answer where you are, not where the bug began.

That is the reusable playbook this lab is designed to teach.
