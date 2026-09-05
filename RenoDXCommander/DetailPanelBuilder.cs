// DetailPanelBuilder.cs — Core scaffolding: class declaration, constructor, current detail card state, and detail panel population.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System.IO;

namespace RenoDXCommander;

/// <summary>
/// Helper class responsible for detail panel population and overrides panel construction.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public partial class DetailPanelBuilder
{
    private readonly MainWindow _window;
    private readonly DispatcherQueue _dispatcherQueue;
    private GameCardViewModel? _currentDetailCard;
    private readonly object _detailRefreshGate = new();
    private readonly HashSet<string> _pendingDetailProperties = new(StringComparer.Ordinal);
    private bool _detailRefreshQueued;
    private GameCardViewModel? _pendingDetailCard;

    // Services injected directly — no longer accessed via ViewModel forwarding properties
    private readonly IGameNameService _gameNameService;
    private readonly IPeHeaderService _peHeaderService;
    private readonly DlssPresetService _dlssPresetService;
    private readonly IDlssStreamlineService _dlssStreamlineService;
    private readonly IDxvkService _dxvkService;
    private readonly IDllOverrideService _dllOverrideService;
    private readonly IAuxInstallService _auxInstallService;
    private readonly IShaderPackService _shaderPackService;
    private readonly IOptiScalerWikiService _optiScalerWikiService;
    private readonly IHdrDatabaseService _hdrDatabaseService;
    private readonly IOptiScalerService _optiScalerService;

    public DetailPanelBuilder(
        MainWindow window,
        IGameNameService gameNameService,
        IPeHeaderService peHeaderService,
        DlssPresetService dlssPresetService,
        IDlssStreamlineService dlssStreamlineService,
        IDxvkService dxvkService,
        IDllOverrideService dllOverrideService,
        IAuxInstallService auxInstallService,
        IShaderPackService shaderPackService,
        IOptiScalerWikiService optiScalerWikiService,
        IHdrDatabaseService hdrDatabaseService,
        IOptiScalerService optiScalerService)
    {
        _window = window;
        _dispatcherQueue = window.DispatcherQueue;
        _gameNameService = gameNameService;
        _peHeaderService = peHeaderService;
        _dlssPresetService = dlssPresetService;
        _dlssStreamlineService = dlssStreamlineService;
        _dxvkService = dxvkService;
        _dllOverrideService = dllOverrideService;
        _auxInstallService = auxInstallService;
        _shaderPackService = shaderPackService;
        _optiScalerWikiService = optiScalerWikiService;
        _hdrDatabaseService = hdrDatabaseService;
        _optiScalerService = optiScalerService;

        // Set hand cursor on link buttons so they feel like clickable links
        var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var cursorProp = typeof(UIElement).GetProperty("ProtectedCursor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        cursorProp?.SetValue(_window.DetailNexusModsBtn, handCursor);
        cursorProp?.SetValue(_window.DetailPcgwBtn, handCursor);
        cursorProp?.SetValue(_window.DetailUwFixBtn, handCursor);
        cursorProp?.SetValue(_window.DetailUltraPlusBtn, handCursor);
        cursorProp?.SetValue(_window.DetailFolderBtn, handCursor);
        cursorProp?.SetValue(_window.DetailAppDataBtn, handCursor);
    }

    /// <summary>Gets the currently displayed detail card (if any).</summary>
    public GameCardViewModel? CurrentDetailCard => _currentDetailCard;

    public void PopulateDetailPanel(GameCardViewModel card)
    {
        // Set DxvkVariantPending so the Install button row shows when a variant is selected but not installed
        card.DxvkVariantPending = !card.DxvkEnabled
            && _window.ViewModel.GetDxvkVariantOverride(card.GameName, card.Source) != null;

        // Unsubscribe from previous card
        if (_currentDetailCard != null)
            _currentDetailCard.PropertyChanged -= DetailCard_PropertyChanged;

        _currentDetailCard = card;
        card.PropertyChanged += DetailCard_PropertyChanged;

        // Header
        _window.DetailGameName.Text = card.GameName;

        // Source badge
        if (card.HasSourceIcon)
        {
            _window.DetailSourceIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(card.SourceIconUri);
            _window.DetailSourceIcon.Visibility = Visibility.Visible;
        }
        else
        {
            _window.DetailSourceIcon.Visibility = Visibility.Collapsed;
        }
        _window.DetailSourceText.Text = card.Source;
        _window.DetailSourceBadge.Visibility = string.IsNullOrEmpty(card.Source) ? Visibility.Collapsed : Visibility.Visible;

        // Engine badge
        if (!string.IsNullOrEmpty(card.EngineHint))
        {
            _window.DetailEngineText.Text = card.EngineHint;
            // Set engine icon
            if (card.EngineHint.IndexOf("Unreal", StringComparison.OrdinalIgnoreCase) >= 0)
                _window.DetailEngineIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/icons/unrealengine.ico"));
            else if (card.EngineHint.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0)
                _window.DetailEngineIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/icons/unity.ico"));
            else
                _window.DetailEngineIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/icons/engine.ico"));
            _window.DetailEngineBadge.Visibility = Visibility.Visible;

            // Clickable when version is vague (no specific number detected) and it's Unreal
            // NOT clickable when: specific version set (e.g. 5.3.2, 4.27.2), or Legacy engine
            bool hasSpecificVersion = System.Text.RegularExpressions.Regex.IsMatch(card.EngineHint, @"Unreal Engine \d");
            bool isVague = card.EngineHint == "Unreal Engine" || card.EngineHint == "Unreal Engine 5";
            bool isClickable = card.EngineHint.IndexOf("Unreal", StringComparison.OrdinalIgnoreCase) >= 0
                && !hasSpecificVersion
                && !card.EngineHint.Contains("Legacy", StringComparison.OrdinalIgnoreCase)
                || (isVague && _gameNameService.EngineVersionOverrides.ContainsKey(card.GameName));
            _window.DetailEngineText.TextDecorations = isClickable ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None;
            _window.DetailEngineBadge.Tag = isClickable ? card : null;
            if (isClickable)
                ToolTipService.SetToolTip(_window.DetailEngineBadge, "Click to cycle engine version (affects DOF Fix eligibility)");
            else
                ToolTipService.SetToolTip(_window.DetailEngineBadge, null);
        }
        else _window.DetailEngineBadge.Visibility = Visibility.Collapsed;

        // Graphics API badge(s) — one per detected API
        UpdateGraphicsApiBadges(_window, card);

        // Generic badge — hidden (redundant with engine badge + UE-Extended toggle)
        _window.DetailGenericBadge.Visibility = Visibility.Collapsed;

        // 32-bit / 64-bit badge
        _window.Detail32BitBadge.Visibility = card.Is32Bit ? Visibility.Visible : Visibility.Collapsed;
        _window.Detail64BitBadge.Visibility = !card.Is32Bit ? Visibility.Visible : Visibility.Collapsed;

        // Wiki status badge — hidden from main UI, shown inside Info button dialog instead
        _window.DetailWikiBadge.Visibility = Visibility.Collapsed;
        _window.DetailSepPlatformStatus.Visibility = Visibility.Collapsed;

        // Author badges
        _window.DetailAuthorBadgePanel.Children.Clear();
        if (card.HasAuthors)
        {
            foreach (var author in card.AuthorList)
            {
                var donationUrl = GameCardViewModel.GetAuthorDonationUrl(author);
                var textBlock = new TextBlock
                {
                    Text = author,
                    FontSize = 11,
                    Foreground = UIFactory.Brush(ResourceKeys.ChipTextBrush),
                    TextDecorations = donationUrl != null ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None,
                };
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 2, 6, 2),
                    Background = UIFactory.Brush(ResourceKeys.ChipDefaultBrush),
                    BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = textBlock,
                };
                if (donationUrl != null)
                {
                    badge.PointerPressed += async (s, e) =>
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(donationUrl));
                    var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
                    var arrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
                    var cursorProp = typeof(UIElement).GetProperty("ProtectedCursor",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    badge.PointerEntered += (s, e) => cursorProp?.SetValue(badge, handCursor);
                    badge.PointerExited += (s, e) => cursorProp?.SetValue(badge, arrowCursor);
                    ToolTipService.SetToolTip(badge, $"Mod author: {author} — click to open Ko-fi donation page");
                }
                else
                {
                    ToolTipService.SetToolTip(badge, $"Mod author: {author}");
                }
                _window.DetailAuthorBadgePanel.Children.Add(badge);
            }
            _window.DetailAuthorBadgePanel.Visibility = Visibility.Visible;
        }
        else
        {
            _window.DetailAuthorBadgePanel.Visibility = Visibility.Collapsed;
        }

        // Install path + installed file
        _window.DetailInstallPath.Text = card.InstallPath;
        if (!string.IsNullOrEmpty(card.InstalledAddonFileName))
        {
            _window.DetailInstalledFile.Text = $"{card.InstalledAddonFileName}";
            _window.DetailInstalledFileBadge.Visibility = Visibility.Visible;
            _window.DetailSepModPlatform.Visibility = Visibility.Visible;
        }
        else
        {
            _window.DetailInstalledFileBadge.Visibility = Visibility.Collapsed;
            _window.DetailSepModPlatform.Visibility = Visibility.Collapsed;
        }

        // Utility buttons — set Tag for event handlers
        _window.DetailFavBtn.Tag = card;
        _window.DetailFavIcon.Text = "Favourite";
        var favColor = card.IsFavourite
            ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
            : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.ChipTextBrush]).Color;
        _window.DetailFavIcon.Foreground = new SolidColorBrush(favColor);
        _window.DetailFavBtn.BorderBrush = card.IsFavourite
            ? new SolidColorBrush(((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color)
            : UIFactory.Brush(ResourceKeys.BorderSubtleBrush);

        _window.DetailHideBtn.Tag = card;
        _window.DetailHideIcon.Text = card.IsHidden ? "Show" : "Hide";
        _window.DetailHideBtn.Foreground = UIFactory.Brush(ResourceKeys.ChipTextBrush);

        // Folder management buttons
        _window.DetailFolderBtn.Tag = card;

        // AppData button — visible only for UE games with a resolvable AppData/Documents config folder
        _window.DetailAppDataBtn.Tag = card;
        var appDataPath = ResolveGameConfigRoot(card);
        _window.DetailAppDataBtn.Visibility = appDataPath != null ? Visibility.Visible : Visibility.Collapsed;

        // PCGW link button
        _window.DetailPcgwBtn.Tag = card;
        _window.DetailPcgwBtn.Visibility = card.HasPcgwUrl ? Visibility.Visible : Visibility.Collapsed;

        // HDR toggle button — show per-game state
        _window.DetailHdrToggleBtn.Tag = card;
        var hdrOverride = _gameNameService.HdrToggleOverrides
            .TryGetValue(card.GameName, out var hov) ? hov : null;
        bool hdrActive = hdrOverride != null
            ? string.Equals(hdrOverride, "On", StringComparison.OrdinalIgnoreCase)
            : _window.ViewModel.Settings.HdrAutoToggle;
        _window.DetailHdrToggleText.Text = "HDR";
        _window.DetailHdrToggleBtn.Background = hdrActive
            ? UIFactory.Brush(ResourceKeys.AccentPurpleBgBrush)
            : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
        _window.DetailHdrToggleBtn.BorderBrush = hdrActive
            ? UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush)
            : UIFactory.Brush(ResourceKeys.BorderSubtleBrush);
        _window.DetailHdrToggleText.Foreground = hdrActive
            ? UIFactory.Brush(ResourceKeys.AccentPurpleBrush)
            : UIFactory.Brush(ResourceKeys.ChipTextBrush);

        // RES toggle button — hidden unless feature enabled
        if (FeatureFlags.ResolutionControl)
        {
            _window.DetailResToggleBtn.Visibility = Visibility.Visible;
            _window.DetailResToggleBtn.Tag = card;
            var resOverride = _gameNameService.ResToggleOverrides
                .TryGetValue(card.GameName, out var rov) ? rov : null;
            bool resActive = resOverride != null
                ? string.Equals(resOverride, "On", StringComparison.OrdinalIgnoreCase)
                : _window.ViewModel.Settings.ResolutionAutoToggle;
            _window.DetailResToggleText.Text = "RES";
            _window.DetailResToggleBtn.Background = resActive
                ? UIFactory.Brush(ResourceKeys.AccentPurpleBgBrush)
                : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
            _window.DetailResToggleBtn.BorderBrush = resActive
                ? UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush)
                : UIFactory.Brush(ResourceKeys.BorderSubtleBrush);
            _window.DetailResToggleText.Foreground = resActive
                ? UIFactory.Brush(ResourceKeys.AccentPurpleBrush)
                : UIFactory.Brush(ResourceKeys.ChipTextBrush);
        }
        else
        {
            _window.DetailResToggleBtn.Visibility = Visibility.Collapsed;
        }

        // Nexus Mods link button
        _window.DetailNexusModsBtn.Tag = card;
        _window.DetailNexusModsBtn.Visibility = card.HasNexusModsUrl ? Visibility.Visible : Visibility.Collapsed;

        // UW Fix link button
        _window.DetailUwFixBtn.Tag = card;
        _window.DetailUwFixBtn.Visibility = card.HasUwFixUrl ? Visibility.Visible : Visibility.Collapsed;
        if (card.HasUwFixUrl)
        {
            var source = card.UwFixSource ?? "creator";
            ToolTipService.SetToolTip(_window.DetailUwFixBtn, $"Open {source}'s ultrawide fix page");
        }

        // Ultra+ link button
        _window.DetailUltraPlusBtn.Tag = card;
        _window.DetailUltraPlusBtn.Visibility = card.HasUltraPlusUrl ? Visibility.Visible : Visibility.Collapsed;

        // Luma toggle removed — both RenoDX and Luma rows always visible when Luma is available
        _window.DetailLumaToggle.Visibility = Visibility.Collapsed;
        _window.DetailLumaInfoText.Visibility = Visibility.Collapsed;

        // Populate component rows
        UpdateDetailComponentRows(card);
    }

    /// <summary>
    /// Resolves the top-level game config directory (for the AppData button).
    /// Checks %LocalAppData%\{projectName}\ and Documents\My Games\{gameName}\.
    /// Returns the path if found, null otherwise.
    /// </summary>
    private static string? ResolveGameConfigRoot(GameCardViewModel card)
    {
        var projectName = card.EngineIniProjectOverride
            ?? AuxInstallService.ResolveUeProjectName(card.InstallPath ?? "");

        // If the override is a full path (or pipe-separated paths), resolve directly
        if (!string.IsNullOrEmpty(card.EngineIniProjectOverride)
            && (card.EngineIniProjectOverride.Contains('\\') || card.EngineIniProjectOverride.Contains('/')))
        {
            var candidates = card.EngineIniProjectOverride.Split('|');
            foreach (var candidate in candidates)
            {
                var expanded = Environment.ExpandEnvironmentVariables(candidate.Trim());
                if (Directory.Exists(expanded)) return expanded;
            }
            return null;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Check %LocalAppData%\{projectName}\
        if (!string.IsNullOrEmpty(projectName))
        {
            var dir = Path.Combine(localAppData, projectName);
            if (Directory.Exists(dir)) return dir;
        }

        // Check Documents\My Games\{gameName}\
        if (!string.IsNullOrEmpty(card.GameName))
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var myGamesDir = Path.Combine(docs, "My Games", card.GameName);
            if (Directory.Exists(myGamesDir)) return myGamesDir;

            // Try stripped name (® ™ ©)
            var stripped = card.GameName.Replace("®", "").Replace("™", "").Replace("©", "").Trim();
            if (stripped != card.GameName)
            {
                myGamesDir = Path.Combine(docs, "My Games", stripped);
                if (Directory.Exists(myGamesDir)) return myGamesDir;
            }
        }

        // Check in-game directory: {GameRoot}\{ProjectName}\Saved\
        if (!string.IsNullOrEmpty(card.InstallPath))
        {
            var normalized = card.InstallPath.Replace('/', '\\').TrimEnd('\\');
            var parts = normalized.Split('\\');
            for (int i = parts.Length - 1; i > 0; i--)
            {
                if (parts[i].Equals("Binaries", StringComparison.OrdinalIgnoreCase))
                {
                    // Project folder is immediately above Binaries
                    var projectDir = string.Join('\\', parts.Take(i));
                    var savedDir = Path.Combine(projectDir, "Saved");
                    if (Directory.Exists(savedDir)) return projectDir;

                    // Also check sibling folders in the game root
                    if (i - 1 > 0)
                    {
                        var gameRoot = string.Join('\\', parts.Take(i - 1));
                        try
                        {
                            foreach (var subDir in Directory.EnumerateDirectories(gameRoot))
                            {
                                var subSaved = Path.Combine(subDir, "Saved");
                                if (Directory.Exists(subSaved)) return subDir;
                            }
                        }
                        catch { }
                    }
                    break;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Clears the graphics API badge panel and adds one badge per detected API.
    /// APIs are shown in a consistent order: DX8 → DX9 → DX10 → DX11 → DX12 → Vulkan → OpenGL.
    /// </summary>
    internal static void UpdateGraphicsApiBadges(MainWindow window, GameCardViewModel card)
    {
        window.DetailGraphicsApiBadgePanel.Children.Clear();

        if (!card.HasGraphicsApiBadge)
        {
            window.DetailGraphicsApiBadgePanel.Visibility = Visibility.Collapsed;
            return;
        }

        var rawApis = card.DetectedApis.Count > 0
            ? card.DetectedApis
            : new HashSet<GraphicsApiType> { card.GraphicsApi };

        // Filter: if any modern API (DX11, DX12, VLK) is present, drop legacy (DX8, DX9, DX10).
        // If DX10 is the highest present, drop DX8/DX9. Priority: DX12 > DX11 > VLK > OGL > DX10 > DX9 > DX8.
        var hasModern = rawApis.Contains(GraphicsApiType.DirectX11)
                     || rawApis.Contains(GraphicsApiType.DirectX12)
                     || rawApis.Contains(GraphicsApiType.Vulkan);
        var hasDx10Plus = hasModern || rawApis.Contains(GraphicsApiType.DirectX10);

        var filtered = rawApis.Where(a => a switch
        {
            GraphicsApiType.DirectX8  => !hasDx10Plus,
            GraphicsApiType.DirectX9  => !hasDx10Plus,
            GraphicsApiType.DirectX10 => !hasModern,
            // OGL only shows alone — if any DX or Vulkan is present it's likely a launcher/helper exe
            GraphicsApiType.OpenGL    => rawApis.Count == 1,
            _                         => true,
        });

        // Display in consistent order: DX8→DX9→DX10→DX11→DX12→VLK→OGL
        var displayOrder = new[]
        {
            GraphicsApiType.DirectX8, GraphicsApiType.DirectX9, GraphicsApiType.DirectX10,
            GraphicsApiType.DirectX11, GraphicsApiType.DirectX12,
            GraphicsApiType.Vulkan, GraphicsApiType.OpenGL,
        };
        var apisToShow = displayOrder.Where(a => filtered.Contains(a)).ToList();

        foreach (var api in apisToShow)
        {
            var label = Services.GraphicsApiDetector.GetLabel(api);
            if (string.IsNullOrEmpty(label)) continue;

            var textBlock = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.ChipTextBrush),
            };
            var badge = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 6, 2),
                Background = UIFactory.Brush(ResourceKeys.ChipDefaultBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = textBlock,
            };
            window.DetailGraphicsApiBadgePanel.Children.Add(badge);
        }

        window.DetailGraphicsApiBadgePanel.Visibility =
            window.DetailGraphicsApiBadgePanel.Children.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
