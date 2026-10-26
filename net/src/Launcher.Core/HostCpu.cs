using System.Runtime.Intrinsics.X86;

namespace Launcher.Core;

/// <summary>
/// Whether this PC's processor runs the game module. The module is compiled for x86-64-v3 (the switches' host
/// features: AVX, AVX2, BMI1, BMI2, FMA, LZCNT, MOVBE, F16C); on a processor without one of them the game would stop
/// at the first such instruction, with no word why. AVX2 is required, so the launcher says so
/// before a start instead.
/// </summary>
public static class HostCpu
{
    /// <summary>What the play buttons say on a processor that lacks them.</summary>
    public const string NotSupported = "Processor without AVX2 · the game can't run on this PC";

    /// <summary>Every feature the module is compiled for is there. .NET's own checks include the operating system's
    /// support for AVX's registers; MOVBE and F16C are read from CPUID leaf 1.</summary>
    public static bool RunsTheGame { get; } = Check();

    private static bool Check()
    {
        if (!X86Base.IsSupported
            || !(Avx2.IsSupported && Bmi1.IsSupported && Bmi2.IsSupported && Fma.IsSupported && Lzcnt.IsSupported))
        {
            return false;
        }
        const int Movbe = 1 << 22, F16c = 1 << 29;
        var (_, _, ecx, _) = X86Base.CpuId(1, 0);
        return (ecx & Movbe) != 0 && (ecx & F16c) != 0;
    }
}
