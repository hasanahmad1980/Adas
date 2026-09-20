using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Adas.App.Shell;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace Adas.App.Views.Pages;

/// <summary>
/// Installed overview page: one row per game that currently has any Adas component deployed, with a
/// per-component summary, a filter box and per-row Open folder / Repair / Remove actions. The engine
/// keeps no persisted swap-history log, so rows are derived live from the card component flags plus a
/// <see cref="Dlss5ComponentService.LoadRecord"/> probe for DLSS 5 neural installs. Repair/Remove reuse
/// the same <see cref="Dlss5Installer"/> engine calls the setup pane uses. Hosted in the MainWindow page host.
/// </summary>
public partial class HistoryPage : UserControl
{
    private readonly MainViewModel? _main;
    private List<InstalledEntry> _all = new();
    private bool _busy;

    public HistoryPage() : this(null) { }

    public HistoryPage(MainViewModel? main)
    {
        InitializeComponent();
        _main = main;
        SearchBox.TextChanged += (_, _) => ApplyFilter();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var cards = _main?.AllCards;
        if (cards is null || cards.Count == 0) { _all = new(); ApplyFilter(); return; }

        // Snapshot the fields we need on the UI thread; probe the record off-thread.
        var snapshot = new List<(GameCardViewModel card, string name, string source, string path, string flagSummary)>();
        foreach (var card in cards)
            snapshot.Add((card, card.GameName, card.Source, card.InstallPath, ComponentSummary(card)));

        var entries = await Task.Run(() =>
        {
            var list = new List<InstalledEntry>();
            foreach (var (card, name, source, path, flagSummary) in snapshot)
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(flagSummary)) parts.Add(flagSummary);

                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    try
                    {
                        if (Dlss5ComponentService.LoadRecord(path) is { } record)
                            parts.Add($"DLSS 5 neural ({record.Profile} / {record.Mode})");
                    }
                    catch { /* damaged/absent record — ignore for the overview */ }
                }

                if (parts.Count == 0) continue; // nothing deployed into this game
                list.Add(new InstalledEntry(
                    string.IsNullOrWhiteSpace(name) ? "(unnamed game)" : name,
                    string.IsNullOrWhiteSpace(source) ? "" : source,
                    string.Join(" · ", parts),
                    path,
                    card));
            }
            return list;
        });

        _all = entries;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = SearchBox.Text?.Trim();
        var shown = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(e =>
                (e.GameName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Source?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Summary?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

        EntriesList.ItemsSource = shown;
        EmptyHint.IsVisible = shown.Count == 0;
        EmptyHint.Text = _all.Count == 0
            ? "Nothing installed yet. Set up a game from the library to see it here."
            : "No installed games match your filter.";
        Subtitle.Text = _all.Count == 0
            ? "No Adas components are currently deployed."
            : $"{_all.Count} game(s) with Adas components currently deployed.";
    }

    private static string ComponentSummary(GameCardViewModel card)
    {
        var parts = new List<string>();
        if (card.IsRdxInstalled) parts.Add("RenoDX");
        if (card.IsRsInstalled) parts.Add("ReShade");
        if (card.IsUlInstalled) parts.Add("ReLimiter");
        if (card.IsDcInstalled) parts.Add("Display Commander");
        if (card.IsOsInstalled) parts.Add("OptiScaler");
        if (card.IsDxvkInstalled) parts.Add("DXVK");
        if (card.IsRefInstalled) parts.Add("REFramework");
        if (card.IsLumaInstalled) parts.Add("Luma");
        return string.Join(", ", parts);
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        var path = (sender as Button)?.Tag as string;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch { /* best effort */ }
    }

    private void OnRepair(object? sender, RoutedEventArgs e) =>
        _ = RunMaintenanceAsync(sender, (main, owner, card, progress) => Dlss5Installer.RepairAsync(main, owner, card, progress));

    private void OnRemove(object? sender, RoutedEventArgs e) =>
        _ = RunMaintenanceAsync(sender, (main, owner, card, progress) => Dlss5Installer.RemoveAsync(main, owner, card, progress));

    private async Task RunMaintenanceAsync(
        object? sender,
        Func<MainViewModel, Window, GameCardViewModel, IProgress<(string message, double percent)>, Task<Dlss5Installer.Outcome>> action)
    {
        if (_busy || _main is null) return;
        if ((sender as Button)?.Tag is not InstalledEntry entry || entry.Card is not { } card) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        _busy = true;
        var progress = new Progress<(string message, double percent)>(u => Subtitle.Text = u.message);
        try
        {
            var outcome = await action(_main, owner, card, progress);
            if (outcome.Ran)
            {
                // Refresh only the affected game's DLSS 5 state instead of rescanning the whole library.
                _main.RefreshCardDlss5State(card, card.InstallPath);
                card.NotifyAll();
                await LoadAsync();
            }
            else
            {
                Subtitle.Text = outcome.Message;
            }
        }
        catch (Exception ex)
        {
            Subtitle.Text = $"Action failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }
}
