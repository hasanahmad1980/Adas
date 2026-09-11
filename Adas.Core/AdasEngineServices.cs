using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Registers the full Adas engine (services + shared infrastructure + framework-neutral
/// ViewModels) into a DI container. Shell-agnostic: the WinUI shell and the Avalonia shell each
/// call <see cref="AddAdasEngine"/> and then add their own window/view types on top.
/// </summary>
public static class AdasEngineServices
{
    public static IServiceCollection AddAdasEngine(this IServiceCollection services)
    {
        // Shared HttpClient — singleton with UserAgent header and optimised connection settings.
        services.AddSingleton<HttpClient>(_ =>
        {
            var handler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                MaxConnectionsPerServer = 16,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                InitialHttp2StreamWindowSize = 1024 * 1024, // 1 MB
            };

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "RHI/2.0");
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestVersion = new Version(2, 0);
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            return client;
        });

        // Shared ETag cache for GitHub API conditional requests (304 Not Modified)
        services.AddSingleton<GitHubETagCache>();

        // Services — all singletons
        services.AddSingleton<IModInstallService, ModInstallService>();
        services.AddSingleton<IAuxInstallService, AuxInstallService>();
        services.AddSingleton<IWikiService, WikiService>();
        services.AddSingleton<IManifestService, ManifestService>();
        services.AddSingleton<IGameLibraryService, GameLibraryService>();
        services.AddSingleton<IReShadeUpdateService, ReShadeUpdateService>();
        services.AddSingleton<INormalReShadeUpdateService, NormalReShadeUpdateService>();
        services.AddSingleton<ReShadeNightlyService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ILumaService, LumaService>();
        services.AddSingleton<IShaderPackService, ShaderPackService>();
        services.AddSingleton<IAddonPackService, AddonPackService>();
        services.AddSingleton<ILiliumShaderService, LiliumShaderService>();
        services.AddSingleton<IGameDetectionService, GameDetectionService>();
        services.AddSingleton<IPeHeaderService, PeHeaderService>();
        services.AddSingleton<ICrashReporter, CrashReporterService>();
        services.AddSingleton<IAuxFileService>(sp => sp.GetRequiredService<IAuxInstallService>() as AuxInstallService
            ?? throw new InvalidOperationException("IAuxInstallService must be AuxInstallService"));
        services.AddSingleton<IREFrameworkService, REFrameworkService>();
        services.AddSingleton<INexusModsService, NexusModsService>();
        services.AddSingleton<INexusUpdateService, NexusUpdateService>();
        services.AddSingleton<ISteamAppIdResolver, SteamAppIdResolver>();
        services.AddSingleton<IPcgwService, PcgwService>();
        services.AddSingleton<IUltrawideFixService, UltrawideFixService>();
        services.AddSingleton<IUltraPlusService, UltraPlusService>();

        // ViewModels
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<FilterViewModel>();

        // Extracted services
        services.AddSingleton<IUpdateOrchestrationService, UpdateOrchestrationService>();
        services.AddSingleton<IDllOverrideService, DllOverrideService>();
        services.AddSingleton<IGameNameService, GameNameService>();
        services.AddSingleton<IGameInitializationService, GameInitializationService>();
        services.AddSingleton<ISevenZipExtractor, ReShadeExtractor>();
        services.AddSingleton<IOptiScalerService, OptiScalerService>();
        services.AddSingleton<IOptiScalerWikiService, OptiScalerWikiService>();
        services.AddSingleton<IHdrDatabaseService, HdrDatabaseService>();
        services.AddSingleton<IDxvkService, DxvkService>();
        // Lazy<IDxvkService> breaks the circular dependency between OptiScalerService ↔ DxvkService
        services.AddSingleton<Lazy<IDxvkService>>(sp => new Lazy<IDxvkService>(() => sp.GetRequiredService<IDxvkService>()));
        services.AddSingleton<IDlssStreamlineService, DlssStreamlineService>();
        services.AddSingleton<DlssPresetService>();
        services.AddSingleton<DofFixService>();
        services.AddSingleton<MfgUnlockService>();
        services.AddSingleton<RtxMfgUnlockService>();
        services.AddSingleton<DiagnosticsBundleService>();
        services.AddSingleton<AutoUpdateService>();
        services.AddSingleton<DlssEnablerService>();
        services.AddSingleton<Renodx5AddonService>();
        services.AddSingleton<DeepFriedChickenService>();
        services.AddSingleton<Dlss5CompatibilityService>();
        services.AddSingleton<Dlss5ComponentService>();
        services.AddSingleton<GameCleanupService>();
        services.AddSingleton<DlssNrRepairService>();
        services.AddSingleton<CustomReShadeHashService>();
        services.AddSingleton<SeenWikiModsService>();
        services.AddSingleton<SeenUltraPlusModsService>();
        services.AddSingleton<SeenLumaModsService>();
        services.AddSingleton<NexusDownloadService>();
        // Lazy<IDlssStreamlineService> breaks the circular dependency between OptiScalerService ↔ DlssStreamlineService
        services.AddSingleton<Lazy<IDlssStreamlineService>>(sp => new Lazy<IDlssStreamlineService>(() => sp.GetRequiredService<IDlssStreamlineService>()));

        services.AddSingleton<MainViewModel>();

        return services;
    }
}
