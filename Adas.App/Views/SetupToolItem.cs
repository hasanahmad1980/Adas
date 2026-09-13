using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using RenoDXCommander.Services;

namespace Adas.App.Views;

/// <summary>One row of the setup page's "Also install" checklist.</summary>
public sealed class SetupToolItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isInstalled;
    private string _notesText = "";
    private IBrush _notesBrush = Brushes.Gray;

    public SetupToolItem(SetupTool tool, Action<SetupToolItem> onSelectionChanged, Action<SetupToolItem> onRemove)
    {
        Tool = tool;
        Name = Dlss5ToolCompatibility.Name(tool);
        Description = Dlss5ToolCompatibility.Description(tool);
        _onSelectionChanged = onSelectionChanged;
        RemoveCommand = new ActionCommand(() => onRemove(this));
    }

    private readonly Action<SetupToolItem> _onSelectionChanged;

    public SetupTool Tool { get; }
    public string Name { get; }
    public string Description { get; }
    public ICommand RemoveCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            _onSelectionChanged(this);
        }
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        set { if (_isInstalled != value) { _isInstalled = value; OnPropertyChanged(); OnPropertyChanged(nameof(CheckBoxText)); } }
    }

    public string CheckBoxText => IsInstalled ? $"{Name}  ·  installed (tick to reinstall)" : Name;

    public string NotesText
    {
        get => _notesText;
        set { if (_notesText != value) { _notesText = value; OnPropertyChanged(); } }
    }

    public IBrush NotesBrush
    {
        get => _notesBrush;
        set { _notesBrush = value; OnPropertyChanged(); }
    }

    /// <summary>Sets <see cref="IsSelected"/> without firing the selection callback (used when seeding).</summary>
    public void SetSelectedSilently(bool value)
    {
        if (_isSelected == value) return;
        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class ActionCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
