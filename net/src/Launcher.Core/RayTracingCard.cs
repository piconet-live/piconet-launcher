using System.Runtime.InteropServices;

namespace Launcher.Core;

/// <summary>Whether this PC's graphics card can ray trace as Aurora does (Settings, Game, "Ray tracing"; the launch
/// file's <c>"auroraRayTracing"</c>). Aurora asks Dawn for the high-performance graphics card on Direct3D 12 and its
/// ray pass needs DirectX ray tracing tier 1.1 (DXR 1.1) there (recompcore's <c>rt_live.cpp</c>, <c>init_device</c>:
/// below it, "[rt] live pass off" and the game's own picture). The same question is asked here once, off the window's
/// thread: DXGI's high-performance adapter, a Direct3D 12 device on it, its <c>D3D12_FEATURE_D3D12_OPTIONS5</c>.
/// No game is started for it; nothing is kept but the answer.</summary>
public sealed record RayTracingCard(bool Can, string? Reason)
{
    /// <summary>The reason beside a box that cannot be ticked, on a card without DirectX 12 ray tracing.</summary>
    public const string NoCard = "Needs a graphics card with DirectX 12 ray tracing";

    /// <summary>The reason on a PC that is not Windows: the game's Aurora traces rays on Direct3D 12 only.</summary>
    public const string NoWindows = "Needs Windows";

    /// <summary>D3D12_RAYTRACING_TIER_1_1: inline ray queries (RayQuery), what Aurora's shaders use.</summary>
    public const int TierNeeded = 11;

    public static RayTracingCard Yes { get; } = new(true, null);

    /// <summary>The answer for the tier a card says (0: none, 10: DXR 1.0, 11: DXR 1.1); a negative number: no
    /// Direct3D 12 device could be made at all.</summary>
    public static RayTracingCard FromTier(int tier) => tier >= TierNeeded ? Yes : new(false, NoCard);

    private static readonly Lazy<Task<RayTracingCard>> Asked = new(() => Task.Run(Ask));

    /// <summary>The check, started on the first call and asked once for the launcher's life.</summary>
    public static Task<RayTracingCard> Check() => Asked.Value;

    /// <summary>The answer once it is in; null while the check runs. A game start does not wait for it: with the
    /// answer not in, the player's setting is written as it stands (Aurora shows the game's own picture on a card
    /// that cannot).</summary>
    public static RayTracingCard? Known => Asked.IsValueCreated && Asked.Value.IsCompletedSuccessfully ? Asked.Value.Result : null;

    private static RayTracingCard Ask()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new(false, NoWindows);
        }
        try
        {
            return FromTier(Direct3D12.RaytracingTier());
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or SEHException or MarshalDirectiveException)
        {
            return new(false, NoCard);
        }
    }
}

/// <summary>The few calls of DXGI and Direct3D 12 the check needs, through the interfaces' tables (no package).</summary>
internal static unsafe partial class Direct3D12
{
    private const int FeatureLevel11 = 0xb000;  // D3D_FEATURE_LEVEL_11_0, Dawn's own floor
    private const int Options5 = 27;  // D3D12_FEATURE_D3D12_OPTIONS5
    private const int HighPerformance = 2;  // DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, as Aurora asks Dawn
    private static readonly Guid Factory6 = new("c1b6694f-ff09-44a9-b03c-77900a0a1d17");
    private static readonly Guid Adapter1 = new("29038f61-3839-4626-91fd-086879011a05");
    private static readonly Guid Device = new("189819f1-1db6-4b57-be54-1821339b85f7");

    /// <summary>The high-performance card's ray tracing tier; -1 when no Direct3D 12 device can be made on it.</summary>
    public static int RaytracingTier()
    {
        nint adapter = HighPerformanceAdapter();
        try
        {
            nint device;
            Guid id = Device;
            if (D3D12CreateDevice(adapter, FeatureLevel11, &id, &device) < 0 || device == 0)
            {
                return -1;
            }
            try
            {
                // D3D12_FEATURE_DATA_D3D12_OPTIONS5: SRVOnlyTiledResourceTier3, RenderPassesTier, RaytracingTier
                int* options = stackalloc int[3];
                var check = (delegate* unmanaged[Stdcall]<nint, int, void*, uint, int>)(*(void***)device)[13];
                return check(device, Options5, options, 12) < 0 ? 0 : options[2];
            }
            finally
            {
                Release(device);
            }
        }
        finally
        {
            Release(adapter);
        }
    }

    /// <summary>DXGI's first adapter by high performance (IDXGIFactory6, Windows 10 1803 on); 0, the default adapter,
    /// where there is none.</summary>
    private static nint HighPerformanceAdapter()
    {
        nint factory;
        Guid id = Factory6;
        if (CreateDXGIFactory2(0, &id, &factory) < 0 || factory == 0)
        {
            return 0;
        }
        try
        {
            nint adapter;
            Guid adapterId = Adapter1;
            var enumerate = (delegate* unmanaged[Stdcall]<nint, uint, int, Guid*, nint*, int>)(*(void***)factory)[29];
            return enumerate(factory, 0, HighPerformance, &adapterId, &adapter) < 0 ? 0 : adapter;
        }
        finally
        {
            Release(factory);
        }
    }

    private static void Release(nint unknown)
    {
        if (unknown != 0)
        {
            _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)unknown)[2])(unknown);
        }
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory2(uint flags, Guid* id, nint* factory);

    [LibraryImport("d3d12.dll")]
    private static partial int D3D12CreateDevice(nint adapter, int minimumFeatureLevel, Guid* id, nint* device);
}
