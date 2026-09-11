using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace Adas.App.Shell;

/// <summary>
/// RenoDX shader-pack picker — the Avalonia port of the WinUI <c>ShaderPopupHelper</c>. Full parity:
/// packs grouped by category with descriptions, per-file exclusions (expandable, tri-state pack
/// checkbox), file-level dependency auto-select, Expand/Deselect-all, and — in the global context —
/// a saved-profiles panel (load / rename / delete / save / new / export / import). Returns the
/// confirmed pack IDs (dependency-expanded) or <c>null</c> on cancel, and persists per-file exclusions
/// via <see cref="IShaderPackService.SetExcludedFiles"/> on confirm.
/// </summary>
public static class ShaderSelectionDialog
{
    public static Task<List<string>?> ShowAsync(Window owner, IShaderPackService service,
        IReadOnlyList<string>? current, bool global)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var packs = service.AvailablePacks;
        var primaryText = global ? "Deploy" : "Confirm";

        var selected = new HashSet<string>(current ?? Enumerable.Empty<string>(), cmp);

        // Re-entrancy guards (mirror the WinUI helper).
        bool profileLoading = false;
        bool packCbInitializing = false;

        var checkBoxes = new List<(string Id, CheckBox Box)>();
        var fileSubPanels = new Dictionary<string, StackPanel>(cmp);
        var expandButtons = new Dictionary<string, Button>(cmp);
        var fileCheckBoxes = new Dictionary<string, List<(string File, CheckBox Box)>>(cmp);

        Dictionary<string, HashSet<string>> includeMap;
        try { includeMap = service.BuildIncludeMap(); }
        catch { includeMap = new Dictionary<string, HashSet<string>>(cmp); }

        // Fallback ownership map for uncached packs — filename → packId.
        var uncachedOwnership = new Dictionary<string, string>(cmp);
        foreach (var (packId, _, _) in packs)
        {
            if (service.IsPackCached(packId)) continue;
            foreach (var file in service.GetPackShaderFiles(new[] { packId }))
                uncachedOwnership.TryAdd(file, packId);
        }

        var list = new StackPanel { Spacing = 4 };

        // ── Expand-all / Deselect-all row ─────────────────────────────────────
        bool allExpanded = false;
        var expandAllBtn = new Button { Content = "Expand All", FontSize = 12, Classes = { "subtle" } };
        var deselectAllBtn = new Button { Content = "Deselect All", FontSize = 12, Classes = { "subtle" } };
        expandAllBtn.Click += (_, _) =>
        {
            allExpanded = !allExpanded;
            foreach (var (pid, sp) in fileSubPanels)
            {
                if (sp.Children.Count == 0) continue;
                sp.IsVisible = allExpanded;
                if (expandButtons.TryGetValue(pid, out var eb)) eb.Content = allExpanded ? "▼" : "▶";
            }
            expandAllBtn.Content = allExpanded ? "Collapse All" : "Expand All";
        };
        deselectAllBtn.Click += (_, _) =>
        {
            profileLoading = true;
            try
            {
                foreach (var (_, box) in checkBoxes) box.IsChecked = false;
                foreach (var (_, fcList) in fileCheckBoxes)
                    foreach (var (_, fcb) in fcList) fcb.IsChecked = false;
                foreach (var (pid, sp) in fileSubPanels)
                {
                    sp.IsVisible = false;
                    if (expandButtons.TryGetValue(pid, out var eb)) eb.Content = "▶";
                }
                allExpanded = false;
                expandAllBtn.Content = "Expand All";
            }
            finally { profileLoading = false; }
        };
        list.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children = { expandAllBtn, deselectAllBtn },
        });

        // ── Packs grouped by category ─────────────────────────────────────────
        foreach (var group in packs.GroupBy(p => p.Category).OrderBy(g => g.Key))
        {
            var header = group.Key switch
            {
                ShaderPackService.PackCategory.Essential => "Essential",
                ShaderPackService.PackCategory.Recommended => "Recommended",
                _ => "Extra",
            };
            list.Children.Add(new TextBlock
            {
                Text = header,
                FontWeight = FontWeight.SemiBold,
                Margin = new Avalonia.Thickness(0, checkBoxes.Count > 0 ? 10 : 4, 0, 4),
                Foreground = Brush("AdasTextPrimaryBrush", owner),
            });

            foreach (var (id, displayName, _) in group)
            {
                var capturedId = id;
                var description = service.GetPackDescription(id);
                var isCached = service.IsPackCached(id);
                var initialExclusions = isCached ? service.GetExcludedFiles(id) : new HashSet<string>(cmp);

                var fileSubPanel = new StackPanel
                {
                    Margin = new Avalonia.Thickness(24, 0, 0, 0),
                    IsVisible = false,
                };
                var fileCbList = new List<(string File, CheckBox Box)>();

                if (isCached)
                {
                    var packIsSelected = selected.Contains(id);
                    foreach (var fileName in service.GetPackShaderFiles(new[] { id }))
                    {
                        var fileCb = new CheckBox
                        {
                            Content = new TextBlock { Text = fileName, FontSize = 12, Foreground = Brush("AdasTextPrimaryBrush", owner) },
                            Margin = new Avalonia.Thickness(0, 1, 0, 1),
                            IsChecked = packIsSelected && !initialExclusions.Contains(fileName),
                        };
                        fileCbList.Add((fileName, fileCb));
                        fileSubPanel.Children.Add(fileCb);
                    }
                }

                fileSubPanels[id] = fileSubPanel;
                fileCheckBoxes[id] = fileCbList;

                bool? packInitial;
                if (!selected.Contains(id)) packInitial = false;
                else if (isCached && initialExclusions.Count > 0) packInitial = null;
                else packInitial = true;

                var packCb = new CheckBox
                {
                    IsThreeState = isCached && fileCbList.Count > 0,
                    IsChecked = packInitial,
                    Margin = new Avalonia.Thickness(0, 2, 0, 2),
                };

                var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                nameRow.Children.Add(new TextBlock
                {
                    Text = displayName,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brush("AdasTextPrimaryBrush", owner),
                });
                if (isCached)
                    nameRow.Children.Add(new TextBlock { Text = "✓", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.MediumSeaGreen });

                if (isCached && fileCbList.Count > 0)
                {
                    var expandBtn = new Button { Content = "▶", FontSize = 10, Classes = { "subtle" }, VerticalAlignment = VerticalAlignment.Center };
                    expandButtons[id] = expandBtn;
                    nameRow.Children.Add(expandBtn);
                    expandBtn.Click += (_, _) =>
                    {
                        var sp = fileSubPanels[capturedId];
                        sp.IsVisible = !sp.IsVisible;
                        expandBtn.Content = sp.IsVisible ? "▼" : "▶";
                    };
                }

                var innerPanel = new StackPanel { MaxWidth = 490 };
                innerPanel.Children.Add(nameRow);
                if (!string.IsNullOrEmpty(description))
                    innerPanel.Children.Add(new TextBlock
                    {
                        Text = description,
                        FontSize = 11,
                        Opacity = 0.6,
                        TextWrapping = TextWrapping.Wrap,
                        Width = 340,
                        Foreground = Brush("AdasTextPrimaryBrush", owner),
                    });
                packCb.Content = innerPanel;

                // Pack-level toggle → cascade to files + required deps.
                packCb.IsCheckedChanged += (_, _) =>
                {
                    if (packCbInitializing || profileLoading) return;
                    if (packCb.IsChecked == true)
                    {
                        foreach (var reqId in service.GetRequiredPacks(capturedId))
                        {
                            var depBox = checkBoxes.FirstOrDefault(c => cmp.Equals(c.Id, reqId)).Box;
                            if (depBox != null && depBox.IsChecked != true) depBox.IsChecked = true;
                        }
                        if (fileCheckBoxes.TryGetValue(capturedId, out var fcl))
                            foreach (var (_, fcb) in fcl) fcb.IsChecked = true;
                    }
                    else if (packCb.IsChecked == false)
                    {
                        if (fileCheckBoxes.TryGetValue(capturedId, out var fcl))
                            foreach (var (_, fcb) in fcl) fcb.IsChecked = false;
                        if (expandButtons.TryGetValue(capturedId, out var eb) && fileSubPanels.TryGetValue(capturedId, out var sp))
                        {
                            sp.IsVisible = false;
                            eb.Content = "▶";
                        }
                    }
                };

                checkBoxes.Add((id, packCb));
                list.Children.Add(packCb);
                if (fileCbList.Count > 0) list.Children.Add(fileSubPanel);

                // File-level toggle → update pack tri-state + auto-select deps.
                foreach (var (fileName, fileCb) in fileCbList)
                {
                    var capturedFile = fileName;
                    var capturedPackCb = packCb;
                    var capturedList = fileCbList;
                    fileCb.IsCheckedChanged += (_, _) =>
                    {
                        if (profileLoading) return;
                        packCbInitializing = true;
                        UpdatePackTriState(capturedPackCb, capturedList);
                        packCbInitializing = false;
                        if (fileCb.IsChecked == true)
                            AutoSelectDependencies(capturedFile, includeMap, checkBoxes, fileCheckBoxes, uncachedOwnership, cmp);
                    };
                }
            }
        }

        var packScroll = new ScrollViewer
        {
            Content = list,
            MaxHeight = 560,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // ── Profiles panel ────────────────────────────────────────────────────
        var profiles = ShaderProfileService.Load();
        int activeProfileIdx = -1;

        var profileListPanel = new StackPanel { Spacing = 2 };
        var profilePanel = new StackPanel { Width = 210, Spacing = 4, Margin = new Avalonia.Thickness(8, 0, 0, 0) };
        profilePanel.Children.Add(new TextBlock
        {
            Text = "Profiles",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 0, 0, 4),
            Foreground = Brush("AdasTextPrimaryBrush", owner),
        });
        profilePanel.Children.Add(new ScrollViewer { Content = profileListPanel, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var statusLabel = new TextBlock { FontSize = 11, IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 4, 0, 0), Foreground = Brushes.MediumSeaGreen };

        List<string> CollectPackIds() => checkBoxes.Where(c => c.Box.IsChecked != false).Select(c => c.Id).ToList();

        Dictionary<string, List<string>> CollectExclusions()
        {
            var result = new Dictionary<string, List<string>>(cmp);
            foreach (var (id, box) in checkBoxes)
            {
                if (box.IsChecked == false) continue;
                if (!fileCheckBoxes.TryGetValue(id, out var fcList) || fcList.Count == 0) continue;
                var excl = fcList.Where(fc => fc.Box.IsChecked != true).Select(fc => fc.File).ToList();
                if (excl.Count > 0) result[id] = excl;
            }
            return result;
        }

        void ApplyProfile(ShaderProfile profile)
        {
            profileLoading = true;
            try
            {
                var sel = new HashSet<string>(profile.SelectedPacks, cmp);
                foreach (var (id, box) in checkBoxes)
                {
                    if (!sel.Contains(id))
                    {
                        box.IsChecked = false;
                        if (fileCheckBoxes.TryGetValue(id, out var fcl0))
                            foreach (var (_, fcb) in fcl0) fcb.IsChecked = false;
                        if (fileSubPanels.TryGetValue(id, out var sp0)) sp0.IsVisible = false;
                        if (expandButtons.TryGetValue(id, out var eb0)) eb0.Content = "▶";
                        continue;
                    }

                    HashSet<string>? excl = null;
                    if (profile.FileExclusions.TryGetValue(id, out var exclList))
                        excl = new HashSet<string>(exclList, cmp);

                    if (fileCheckBoxes.TryGetValue(id, out var fcList) && fcList.Count > 0)
                    {
                        foreach (var (fileName, fcb) in fcList)
                            fcb.IsChecked = excl == null || !excl.Contains(fileName);
                        int checkedCount = fcList.Count(fc => fc.Box.IsChecked == true);
                        box.IsChecked = checkedCount == fcList.Count ? true : checkedCount == 0 ? false : (bool?)null;
                    }
                    else box.IsChecked = true;
                }
            }
            finally { profileLoading = false; }
        }

        Action rebuildProfileList = null!;
        rebuildProfileList = () =>
        {
            profileListPanel.Children.Clear();
            for (int i = 0; i < profiles.Count; i++)
            {
                var idx = i;
                var prof = profiles[i];
                bool isActive = idx == activeProfileIdx;

                var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
                var nameBtn = new Button
                {
                    Content = prof.Name,
                    FontSize = 12,
                    Classes = { "subtle" },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                nameBtn.Click += (_, _) =>
                {
                    activeProfileIdx = idx;
                    ApplyProfile(profiles[idx]);
                    rebuildProfileList();
                };
                Grid.SetColumn(nameBtn, 0);
                rowGrid.Children.Add(nameBtn);

                if (global)
                {
                    var editBtn = new Button { Content = "✎", FontSize = 11, Classes = { "subtle" }, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
                    editBtn.Click += async (_, _) =>
                    {
                        var newName = await PromptForNameAsync(owner, "Rename profile", profiles[idx].Name);
                        if (!string.IsNullOrWhiteSpace(newName))
                        {
                            profiles[idx].Name = newName.Trim();
                            ShaderProfileService.Save(profiles);
                        }
                        rebuildProfileList();
                    };
                    Grid.SetColumn(editBtn, 1);
                    rowGrid.Children.Add(editBtn);

                    var delBtn = new Button { Content = "✕", FontSize = 10, Classes = { "subtle" }, VerticalAlignment = VerticalAlignment.Center };
                    delBtn.Click += (_, _) =>
                    {
                        profiles.RemoveAt(idx);
                        if (activeProfileIdx == idx) activeProfileIdx = -1;
                        else if (activeProfileIdx > idx) activeProfileIdx--;
                        ShaderProfileService.Save(profiles);
                        rebuildProfileList();
                    };
                    Grid.SetColumn(delBtn, 2);
                    rowGrid.Children.Add(delBtn);
                }

                profileListPanel.Children.Add(new Border
                {
                    CornerRadius = new Avalonia.CornerRadius(4),
                    BorderThickness = new Avalonia.Thickness(isActive ? 1 : 0),
                    BorderBrush = isActive ? Brushes.Teal : Brushes.Transparent,
                    Margin = new Avalonia.Thickness(0, 1, 0, 1),
                    Child = rowGrid,
                });
            }
        };
        rebuildProfileList();

        if (global)
        {
            var saveBtn = new Button { Content = "Save", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Avalonia.Thickness(0, 6, 0, 2) };
            saveBtn.Click += (_, _) =>
            {
                var packIds = CollectPackIds();
                var excls = CollectExclusions();
                if (activeProfileIdx >= 0 && activeProfileIdx < profiles.Count)
                {
                    profiles[activeProfileIdx].SelectedPacks = packIds;
                    profiles[activeProfileIdx].FileExclusions = excls;
                }
                else
                {
                    int n = profiles.Count + 1;
                    while (profiles.Any(p => p.Name.Equals($"Profile {n}", StringComparison.OrdinalIgnoreCase))) n++;
                    profiles.Add(new ShaderProfile { Name = $"Profile {n}", SelectedPacks = packIds, FileExclusions = excls });
                    activeProfileIdx = profiles.Count - 1;
                }
                ShaderProfileService.Save(profiles);
                rebuildProfileList();
            };
            profilePanel.Children.Add(saveBtn);

            var newBtn = new Button { Content = "New", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Avalonia.Thickness(0, 2, 0, 2) };
            newBtn.Click += async (_, _) =>
            {
                int n = profiles.Count + 1;
                while (profiles.Any(p => p.Name.Equals($"Profile {n}", StringComparison.OrdinalIgnoreCase))) n++;
                var name = await PromptForNameAsync(owner, "New profile", $"Profile {n}");
                if (string.IsNullOrWhiteSpace(name)) return;
                profiles.Add(new ShaderProfile { Name = name.Trim(), SelectedPacks = CollectPackIds(), FileExclusions = CollectExclusions() });
                activeProfileIdx = profiles.Count - 1;
                ShaderProfileService.Save(profiles);
                rebuildProfileList();
            };
            profilePanel.Children.Add(newBtn);

            profilePanel.Children.Add(new Border { Height = 1, Margin = new Avalonia.Thickness(0, 4, 0, 4), Background = Brush("AdasSurfaceBrush", owner), Opacity = 0.5 });

            var exportBtn = new Button { Content = "Export…", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            exportBtn.Click += async (_, _) =>
            {
                try
                {
                    var packIds = CollectPackIds();
                    var exclDict = CollectExclusions().ToDictionary(k => k.Key, k => new HashSet<string>(k.Value, cmp), cmp);
                    var profileForExport = activeProfileIdx >= 0 && activeProfileIdx < profiles.Count
                        ? profiles[activeProfileIdx]
                        : new ShaderProfile { Name = "Exported Profile", SelectedPacks = packIds, FileExclusions = exclDict.ToDictionary(k => k.Key, k => k.Value.ToList()) };

                    var zipPath = await Task.Run(() => ShaderProfileService.BuildExportZip(packIds, exclDict, service, profileForExport));
                    var dest = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Export shader profile",
                        SuggestedFileName = Path.GetFileName(zipPath),
                        DefaultExtension = "zip",
                        FileTypeChoices = new[] { new FilePickerFileType("ZIP archive") { Patterns = new[] { "*.zip" } } },
                    });
                    if (dest is null) return;
                    File.Copy(zipPath, dest.Path.LocalPath, overwrite: true);
                    ShowStatus(statusLabel, $"Exported to {dest.Name}", ok: true);
                }
                catch (Exception ex) { ShowStatus(statusLabel, $"Export failed: {ex.Message}", ok: false); }
            };
            profilePanel.Children.Add(exportBtn);

            var importBtn = new Button { Content = "Import…", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Avalonia.Thickness(0, 2, 0, 0) };
            importBtn.Click += async (_, _) =>
            {
                try
                {
                    var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "Import shader profile",
                        AllowMultiple = false,
                        FileTypeFilter = new[] { new FilePickerFileType("ZIP archive") { Patterns = new[] { "*.zip" } } },
                    });
                    var zipPath = picked?.FirstOrDefault()?.Path.LocalPath;
                    if (string.IsNullOrEmpty(zipPath)) return;

                    var result = await Task.Run(() => ShaderProfileService.ImportFromZip(zipPath, service));
                    if (result is null) { ShowStatus(statusLabel, "Invalid archive — not an RHI shader profile.", ok: false); return; }

                    var (importedProfile, extractedPackIds) = result.Value;
                    var importName = importedProfile.Name;
                    int suffix = 1;
                    while (profiles.Any(p => p.Name.Equals(importName, StringComparison.OrdinalIgnoreCase)))
                        importName = $"{importedProfile.Name} ({suffix++})";
                    importedProfile.Name = importName;

                    profiles.Add(importedProfile);
                    activeProfileIdx = profiles.Count - 1;
                    ShaderProfileService.Save(profiles);
                    rebuildProfileList();
                    ApplyProfile(importedProfile);
                    ShowStatus(statusLabel, extractedPackIds.Count > 0
                        ? $"Imported — {extractedPackIds.Count} pack(s) extracted."
                        : "Imported.", ok: true);
                }
                catch (Exception ex) { ShowStatus(statusLabel, $"Import failed: {ex.Message}", ok: false); }
            };
            profilePanel.Children.Add(importBtn);
            profilePanel.Children.Add(statusLabel);
        }

        // ── Two-column content ────────────────────────────────────────────────
        var contentGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        Grid.SetColumn(packScroll, 0);
        contentGrid.Children.Add(packScroll);
        var sep = new Border { Width = 1, Margin = new Avalonia.Thickness(8, 0, 0, 0), Background = Brush("AdasSurfaceBrush", owner), Opacity = 0.3, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(sep, 1);
        contentGrid.Children.Add(sep);
        Grid.SetColumn(profilePanel, 2);
        contentGrid.Children.Add(profilePanel);

        var primary = new Button { Content = primaryText, MinWidth = 110, Classes = { "accent" } };
        var close = new Button { Content = "Cancel", MinWidth = 90 };

        var root = new Border
        {
            Padding = new Avalonia.Thickness(20),
            Background = Brush("AdasSurfaceBrush", owner),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Select shader packs", FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = Brush("AdasTextPrimaryBrush", owner) },
                    contentGrid,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Avalonia.Thickness(0, 12, 0, 0),
                        Children = { close, primary },
                    },
                },
            },
        };

        var dialog = new Window
        {
            Title = "Select shader packs",
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Brush("AdasBackgroundBrush", owner),
        };

        List<string>? result = null;
        primary.Click += (_, _) =>
        {
            var confirmed = new List<string>();
            foreach (var (id, box) in checkBoxes)
            {
                if (box.IsChecked == false) continue;
                confirmed.Add(id);
                if (!fileCheckBoxes.TryGetValue(id, out var fcList) || fcList.Count == 0)
                {
                    service.SetExcludedFiles(id, Array.Empty<string>());
                    continue;
                }
                var excludedFiles = fcList.Where(fc => fc.Box.IsChecked != true).Select(fc => fc.File).ToList();
                service.SetExcludedFiles(id, excludedFiles);
            }
            result = service.ExpandPackDependencies(confirmed).Distinct(cmp).ToList();
            dialog.Close();
        };
        close.Click += (_, _) => { result = null; dialog.Close(); };

        var tcs = new TaskCompletionSource<List<string>?>();
        dialog.Closed += (_, _) => tcs.TrySetResult(result);
        _ = dialog.ShowDialog(owner);
        return tcs.Task;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void UpdatePackTriState(CheckBox packCb, List<(string File, CheckBox Box)> fileCbList)
    {
        if (fileCbList.Count == 0) return;
        int checkedCount = fileCbList.Count(fc => fc.Box.IsChecked == true);
        packCb.IsChecked = checkedCount == fileCbList.Count ? true : checkedCount == 0 ? false : (bool?)null;
    }

    private static void AutoSelectDependencies(
        string checkedFile,
        Dictionary<string, HashSet<string>> includeMap,
        List<(string Id, CheckBox Box)> checkBoxes,
        Dictionary<string, List<(string File, CheckBox Box)>> fileCheckBoxes,
        Dictionary<string, string> uncachedOwnership,
        StringComparer cmp)
    {
        if (!includeMap.TryGetValue(checkedFile, out var deps)) return;
        foreach (var dep in deps)
        {
            string? depPackId = null;
            CheckBox? depFileCb = null;
            foreach (var (packId, fcList) in fileCheckBoxes)
            {
                var match = fcList.FirstOrDefault(fc => cmp.Equals(fc.File, dep));
                if (match.Box != null) { depPackId = packId; depFileCb = match.Box; break; }
            }
            if (depPackId == null) uncachedOwnership.TryGetValue(dep, out depPackId);
            if (depPackId == null) continue;

            var packBox = checkBoxes.FirstOrDefault(c => cmp.Equals(c.Id, depPackId)).Box;
            if (packBox != null && packBox.IsChecked != true) packBox.IsChecked = true;
            if (depFileCb != null && depFileCb.IsChecked != true) depFileCb.IsChecked = true;
        }
    }

    private static void ShowStatus(TextBlock label, string text, bool ok)
    {
        label.Text = text;
        label.Foreground = ok ? Brushes.MediumSeaGreen : Brushes.IndianRed;
        label.IsVisible = true;
    }

    /// <summary>Small modal name prompt used for profile create/rename (no ContentDialog in Avalonia).</summary>
    private static Task<string?> PromptForNameAsync(Window owner, string title, string initial)
    {
        var box = new TextBox { Text = initial, Width = 240 };
        var ok = new Button { Content = "OK", MinWidth = 80, Classes = { "accent" } };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        var win = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Brush("AdasBackgroundBrush", owner),
            Content = new Border
            {
                Padding = new Avalonia.Thickness(18),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = Brush("AdasTextPrimaryBrush", owner) },
                        box,
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } },
                    },
                },
            },
        };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; win.Close(); };
        cancel.Click += (_, _) => { result = null; win.Close(); };
        var tcs = new TaskCompletionSource<string?>();
        win.Closed += (_, _) => tcs.TrySetResult(result);
        _ = win.ShowDialog(owner);
        return tcs.Task;
    }

    private static IBrush Brush(string key, Window owner)
        => owner.TryFindResource(key, out var r) && r is IBrush b ? b : Brushes.Gray;
}
