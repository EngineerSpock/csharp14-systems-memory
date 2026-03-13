using Csharp14.SystemsMemory.StackSpan.Examples;

Console.WriteLine("Module 01: stackalloc, Span<T>, ref struct");
Console.WriteLine();

TelemetryPacketDemo.Run();
Console.WriteLine();

PooledBufferDemo.Run();
Console.WriteLine();

await PooledBufferDemo.RunAsync();
