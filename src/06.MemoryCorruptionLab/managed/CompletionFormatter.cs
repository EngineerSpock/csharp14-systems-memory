using System.Runtime.InteropServices;

using Csharp14.SystemsMemory.MemoryCorruptionLab.Interop;

namespace Csharp14.SystemsMemory.MemoryCorruptionLab;

internal static class CompletionFormatter
{
    public static string Format(NativeCompletion completion)
    {
        return $"""
Completion:
  token             : 0x{completion.Token:X}
  status            : {completion.Status}
  submitted length  : {completion.SubmittedLength}
  copied length     : {completion.CopiedLength}
  observed checksum : 0x{completion.ObservedChecksum:X8}
  frame address     : 0x{completion.FrameAddress:X}
  note1             : 0x{completion.Note1:X}
  note2             : 0x{completion.Note2:X}
""";
    }
}
