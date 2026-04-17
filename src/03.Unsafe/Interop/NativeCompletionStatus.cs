namespace Csharp14.SystemsMemory.UnsafeModule.Interop;

internal enum NativeCompletionStatus : uint
{
    Ok = 0,
    BadArgument = 1,
    BadHeader = 2,
    TrailerCorrupted = 4,
    InternalError = 5
}
