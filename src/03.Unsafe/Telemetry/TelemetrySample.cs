using System.Runtime.InteropServices;

namespace Csharp14.SystemsMemory.UnsafeModule.Telemetry;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct TelemetrySample
{
    public int SensorId;
    public int MilliUnits;
    public long TimestampDeltaUs;
}
