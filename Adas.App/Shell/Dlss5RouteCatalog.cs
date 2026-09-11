using System.Collections.Generic;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace Adas.App.Shell;

/// <summary>One selectable DLSS 5 install route (profile) with its availability verdict for a game.</summary>
public sealed record RouteOption(
    Dlss5InstallProfile Profile,
    string Label,
    string Description,
    bool Supported,
    bool Recommended,
    string StatusText,
    bool Installed = false);

/// <summary>
/// Builds the per-game route list — every route shown, recommended marked, incompatible flagged with
/// a reason. This is the Avalonia rebuild of the WinUI detail-panel profile selector; the availability
/// predicates and copy are ported verbatim from <c>MainWindow.Events.Dlss5.cs</c> so behaviour matches.
/// </summary>
public static class Dlss5RouteCatalog
{
    public static IReadOnlyList<RouteOption> Build(Dlss5Assessment assessment, Dlss5InstallProfile recommended,
        Dlss5InstallProfile? installedProfile = null)
    {
        var mode = assessment.Mode;
        var is64 = assessment.Is64Bit;

        var stableSupported = mode != Dlss5DeploymentMode.Dx10Feeder
            && !(mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder
                    or Dlss5DeploymentMode.Dx9ViaDxvkFeeder
                 && !is64);
        var unifiedSupported = is64 && mode != Dlss5DeploymentMode.NativeVulkan;
        var feederSupported = Dlss5CompatibilityService.IsFeederMode(mode);
        var aioSupported = Dlss5ComponentService.SupportsAio(mode, is64);
        var openGlSupported = Dlss5ComponentService.SupportsOpenGlBridge(mode, is64);
        var upstreamSupported = Dlss5ComponentService.SupportsNeuralUpstream(mode, is64);
        var optiSupported = Dlss5ComponentService.SupportsOptiScalerNr(mode, is64, false);
        var splitSupported = Dlss5ComponentService.SupportsOptiScalerNr(mode, is64, true);
        var multipassSupported = splitSupported;

        var list = new List<RouteOption>();

        void Add(Dlss5InstallProfile profile, string label, string description, bool supported, string unsupportedReason)
        {
            bool isInstalled = installedProfile == profile;
            bool isRecommended = supported && profile == recommended;
            string status = isInstalled
                ? "✓ Installed — currently active for this game."
                : supported
                    ? isRecommended
                        ? "✓ Recommended for this game's detected renderer and architecture."
                        : "Available — experimental; use only when you specifically need this route."
                    : $"✕ Not recommended — {unsupportedReason}";
            list.Add(new RouteOption(profile, label, description, supported, isRecommended, status, isInstalled));
        }

        Add(Dlss5InstallProfile.MaximumQuality,
            "Recommended (stable)",
            "For most games. Adas chooses the renderer-specific stable RenoDX or Feeder pairing and keeps the installation reversible.",
            stableSupported,
            is64 ? "this route needs the matched Feeder beta for the detected 32-bit/translated path"
                 : "this game needs the matched Feeder beta host route");

        Add(Dlss5InstallProfile.ExperimentalUnified,
            "ShortFuse unified (experimental)",
            "For 64-bit DirectX games when you want ShortFuse's combined RenoDX controls; not the native Vulkan mirror route.",
            unifiedSupported,
            !is64 ? "ShortFuse requires a 64-bit game"
                  : "ShortFuse is not compatible with the native Vulkan mirror route");

        Add(Dlss5InstallProfile.LatestFeederBeta,
            $"Feeder {Dlss5ComponentService.BundledFeederBetaVersion} (beta)",
            "For Feeder games without a native DLSS path, legacy/translated renderers, and matched 32-bit hosting.",
            feederSupported,
            "this game is on a native route; Feeder is a transport for games that need it");

        Add(Dlss5InstallProfile.StandaloneAio,
            $"Standalone AIO {Dlss5ComponentService.AioVersion} (experimental)",
            "For supported 64-bit native DLSS games — standalone DLSS-NR plus DLAA/upscaling and optional frame generation. Turn the game's own DLSS/FG off.",
            aioSupported,
            !is64 ? "Standalone AIO requires a 64-bit game"
                  : "Standalone AIO does not support this translated or legacy renderer");

        Add(Dlss5InstallProfile.OpenGlBridge,
            $"OpenGL Bridge {Dlss5ComponentService.OpenGlBridgeVersion} (experimental)",
            "For 64-bit OpenGL games. Native OpenGL DLAA through the bridge; not a DirectX/Vulkan Feeder route.",
            openGlSupported,
            !is64 ? "the OpenGL bridge requires a 64-bit game"
                  : "the detected renderer is not OpenGL");

        Add(Dlss5InstallProfile.NeuralUpstream,
            $"Neural Upstream {Dlss5ComponentService.NeuralUpstreamVersion} (beta)",
            "For 64-bit native DirectX 12 games that already use DLSS. Runs Neural Rendering before the game's own DLSS Super Resolution; never combine with another NGX consumer.",
            upstreamSupported,
            !is64 ? "Neural Upstream requires a 64-bit game"
                  : mode != Dlss5DeploymentMode.NativeDirectX12
                      ? "Neural Upstream requires native DirectX 12"
                      : "the game does not expose the native DLSS contract it needs");

        Add(Dlss5InstallProfile.OptiScalerNeuralRendering,
            $"OptiScaler DLSS-NR {Dlss5ComponentService.OptiScalerNrVersion} (experimental)",
            "For 64-bit native-DLSS DX11, DX12, or Vulkan games — OptiScaler's Insert controls and DLSS-NR pipeline.",
            optiSupported,
            !is64 ? "OptiScaler DLSS-NR requires a 64-bit game"
                  : "OptiScaler DLSS-NR requires a native DLSS DX11, DX12, or Vulkan route");

        Add(Dlss5InstallProfile.OptiScalerNrBeforeSr,
            "NR before upscaling (experimental)",
            "For 64-bit native DirectX 12 games that specifically need Neural Rendering before Super Resolution. Highest-risk experimental fork.",
            splitSupported,
            !is64 ? "the NR-before-upscaling fork requires a 64-bit game"
                  : "the NR-before-upscaling fork requires native DirectX 12");

        Add(Dlss5InstallProfile.OptiScalerPreSrMultipass,
            $"NR before upscaling — multipass {Dlss5ComponentService.OptiScalerMultipassVersion} (experimental)",
            "wilsjo2 pre-SR fork: NR before Super Resolution, 1-3 pass processing and FP8/NVFP4-hybrid precision. Needs a separately supplied nvngx_dlssnr.dll runtime.",
            multipassSupported,
            !is64 ? "the pre-SR multipass fork requires a 64-bit game"
                  : "the pre-SR multipass fork requires native DirectX 12");

        return list;
    }

    /// <summary>
    /// The route Adas recommends for a fresh install, ported from the WinUI shell's selection switch.
    /// <paramref name="installedProfile"/> seeds it (an existing install of the same mode keeps its profile);
    /// pass <see cref="Dlss5InstallProfile.MaximumQuality"/> when nothing is installed so it falls to the
    /// renderer/architecture-derived default.
    /// </summary>
    public static Dlss5InstallProfile Recommend(Dlss5Assessment assessment, Dlss5InstallProfile installedProfile)
    {
        var mode = assessment.Mode;
        var is64 = assessment.Is64Bit;
        return installedProfile switch
        {
            Dlss5InstallProfile.OpenGlBridge when Dlss5ComponentService.SupportsOpenGlBridge(mode, is64)
                => Dlss5InstallProfile.OpenGlBridge,
            Dlss5InstallProfile.NeuralUpstream when Dlss5ComponentService.SupportsNeuralUpstream(mode, is64)
                => Dlss5InstallProfile.NeuralUpstream,
            Dlss5InstallProfile.OptiScalerNeuralRendering => Dlss5InstallProfile.OptiScalerNeuralRendering,
            Dlss5InstallProfile.OptiScalerNrBeforeSr => Dlss5InstallProfile.OptiScalerNrBeforeSr,
            Dlss5InstallProfile.OptiScalerPreSrMultipass => Dlss5InstallProfile.OptiScalerPreSrMultipass,
            Dlss5InstallProfile.StandaloneAio when Dlss5ComponentService.SupportsAio(mode, is64)
                => Dlss5InstallProfile.StandaloneAio,
            Dlss5InstallProfile.ExperimentalUnified when is64 && mode != Dlss5DeploymentMode.NativeVulkan
                => Dlss5InstallProfile.ExperimentalUnified,
            Dlss5InstallProfile.LatestFeederBeta when Dlss5CompatibilityService.IsFeederMode(mode)
                => Dlss5InstallProfile.LatestFeederBeta,
            _ when mode == Dlss5DeploymentMode.Dx10Feeder
                || (mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder
                        or Dlss5DeploymentMode.Dx9ViaDxvkFeeder
                    && !is64)
                => Dlss5InstallProfile.LatestFeederBeta,
            _ => Dlss5InstallProfile.MaximumQuality,
        };
    }
}
