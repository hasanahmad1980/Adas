using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Adas.App.Shell;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace Adas.App.Views;

/// <summary>
/// Installed overview ("History"): one row per game that currently has any Adas component deployed,
/// with a per-component summary. The engine keeps no persisted swap-history log, so this is derived
/// live from the card component flags plus a <see cref="Dlss5ComponentService.LoadRecord"/> probe for
/// DLSS 5 neural installs. Avalonia rebuild of the WinUI Swapper-style history view.
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly MainViewModel? _main;

    public HistoryWindow() : this(null) { }

    public HistoryWindow(MainViewModel? main)
    {
        InitializeComponent();
        _main = main;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var cards = _main?.AllCards;
        if (cards is null || cards.Count == 0) { ShowEntries(new List<InstalledEntry>()); return; }

        // Snapshot the fields we need on the UI thread; probe the record off-thread.
        var snapshot = new List<(string name, string source, string path, string flagSummary)>();
        foreach (var card in cards)
        {
            var flags = ComponentSummary(card);
            if (string.IsNullOrEmpty(flags) && card.InstalledRecord is null)
            {
                // No component flag set — still worth probing for a DLSS 5 record below only if a path exists.
            }
            snapshot.Add((card.GameName, card.Source, card.InstallPath, flags));
        }

        var entries = await Task.Run(() =>
        {
            var list = new List<InstalledEntry>();
            foreach (var (name, source, path, flagSummary) in snapshot)
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
                    path));
            }
            return list;
        });

        ShowEntries(entries);
    }

    private void ShowEntries(List<InstalledEntry> entries)
    {
        EntriesList.ItemsSource = entries;
        EmptyHint.IsVisible = entries.Count == 0;
        Subtitle.Text = entries.Count == 0
            ? "No Adas components are currently deployed."
            : $"{entries.Count} game(s) with Adas components currently deployed.";
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
}
