namespace RenoDXCommander.Models;

public enum Dlss5DeploymentMode
{
    None,
    NativeDirectX12,
    // Uses the former bridge value so existing numeric install records migrate cleanly.
    NativeDirectX11,
    Dx11Feeder,
    Dx12Feeder,
    VulkanFeeder,
    Dx9Feeder,
    // Added at the end so existing numeric install records keep their meaning.
    OpenGlFeeder,
    // The current x64 Feeder still has no DirectX 10 backend. Adas translates
    // rare 64-bit D3D10 games to Vulkan and uses the Vulkan transport.
    Dx10ViaDxvkFeeder,
    // Added at the end so existing numeric install records keep their meaning.
    // Mirrors a Vulkan game's native NGX DLSS contract onto D3D12.
    NativeVulkan,
    Dx8Feeder,
    // Feeder 0.14 retains the private D3D11 relay and adds actionable crash/driver diagnostics.
    // Appended so existing numeric install records keep their meaning.
    Dx10Feeder,
    // Per-game recovery route for 32-bit D3D9 titles where the managed
    // dgVoodoo -> D3D11 -> ReShade chain exits with STATUS_STACK_OVERFLOW.
    // Appended so existing numeric install records keep their meaning.
    Dx9ViaDxvkFeeder,
}

public enum Dlss5InstallProfile
{
    MaximumQuality,
    ExperimentalUnified,
    // Latest upstream Feeder test build. Kept separate from the stable default.
    LatestFeederBeta,
    // Standalone presentation pipeline; never mix with Feeder/Bridge/RenoDX DLSS.
    StandaloneAio,
    // Alternative native-upscaler pipelines; numeric values append for record compatibility.
    OptiScalerNeuralRendering,
    OptiScalerNrBeforeSr,
    // Native 64-bit OpenGL DLAA bridge. Appended for install-record compatibility.
    OpenGlBridge,
}

public sealed record Dlss5Probe
{
    public string GameName { get; init; } = "";
    public string? DeploymentPath { get; init; }
    public bool HasAmbiguousDeploymentPath { get; init; }
    public GraphicsApiType GraphicsApi { get; init; }
    public string GraphicsApiEvidence { get; init; } = "";
    public IReadOnlyList<GraphicsApiType> SupportedGraphicsApis { get; init; } = Array.Empty<GraphicsApiType>();
    public IReadOnlyList<string> InstallationIssues { get; init; } = Array.Empty<string>();
    public bool OpenXrDetected { get; init; }
    public bool Is64Bit { get; init; }
    public string GpuName { get; init; } = "";
    public bool HasNativeDlss { get; init; }
    public bool HasReShadeAddonSupport { get; init; }
    public bool HasRenoDx5Addon { get; init; }
    public bool HasNvngxDlssNr { get; init; }
    public bool HasNvngxDlss { get; init; }
    public bool HasMotionVectorProvider { get; init; }
    public bool HasLegacyTranslation { get; init; }
    public bool PreferDxvkForDirectX9 { get; init; }
    public IReadOnlyList<string> AntiCheatEvidence { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MultiplayerEvidence { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingRuntimeArchitectures { get; init; } = Array.Empty<string>();
}

public sealed record Dlss5Assessment(
    Dlss5DeploymentMode Mode,
    string? DeploymentPath,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> MissingRequirements,
    bool SinglePlayerConfirmed,
    bool Is64Bit)
{
    public bool CanInstall => Mode != Dlss5DeploymentMode.None
                              && BlockingReasons.Count == 0
                              && SinglePlayerConfirmed;

    public string ModeLabel => Mode switch
    {
        Dlss5DeploymentMode.NativeDirectX12 => "Native DirectX 12",
        Dlss5DeploymentMode.NativeDirectX11 => "Native DirectX 11",
        Dlss5DeploymentMode.Dx11Feeder => "DirectX 11 Feeder",
        Dlss5DeploymentMode.Dx12Feeder => "DirectX 12 Feeder",
        Dlss5DeploymentMode.VulkanFeeder => "Vulkan Feeder",
        Dlss5DeploymentMode.Dx9Feeder => "DirectX 9 Feeder (dgVoodoo2)",
        Dlss5DeploymentMode.Dx9ViaDxvkFeeder => "DirectX 9 through DXVK + Vulkan Feeder (automatic recovery)",
        Dlss5DeploymentMode.Dx8Feeder => "DirectX 8 Feeder (32-bit dgVoodoo2)",
        Dlss5DeploymentMode.OpenGlFeeder => "OpenGL Feeder",
        Dlss5DeploymentMode.Dx10ViaDxvkFeeder => "DirectX 10 through DXVK + Feeder",
        Dlss5DeploymentMode.Dx10Feeder => "DirectX 10 Feeder (native 32-bit relay)",
        Dlss5DeploymentMode.NativeVulkan => "Native Vulkan DLSS mirror",
        _ => "Unsupported",
    };
}

public enum Dlss5PathResolutionKind
{
    Missing,
    Resolved,
    Ambiguous,
}

public sealed record Dlss5PathResolution(
    Dlss5PathResolutionKind Kind,
    string? Path,
    IReadOnlyList<string> Candidates);

internal enum Dlss5RenoDxPackage
{
    ExperimentalUnified,
    Native470,
    Feeder455,
}

internal sealed record Dlss5CompatibilityPlan(
    Dlss5RenoDxPackage RenoDxPackage,
    bool InstallFeeder,
    bool InstallDx11Bridge,
    bool PatchFeederForUnifiedName,
    string ProfileName)
{
    public bool UsesExperimentalUnified => RenoDxPackage == Dlss5RenoDxPackage.ExperimentalUnified;
    public bool UsesLatestFeederBeta { get; init; }
    public bool InstallOpenGlBridge { get; init; }
}
