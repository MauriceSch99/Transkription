using System.ComponentModel;
using System.Runtime.CompilerServices;
using Transkription.Settings;

namespace Transkription.Core;

/// <summary>Eintrag in der Dateiliste des Hauptfensters.</summary>
public sealed class FileItem : INotifyPropertyChanged
{
    private bool _isCurrent;

    public FileItem(FileEntry entry)
    {
        Entry = entry;
        Exists = File.Exists(entry.Path);
    }

    public FileEntry Entry { get; }

    public string Name => Path.GetFileName(Entry.Path);
    public string Folder => Path.GetDirectoryName(Entry.Path) ?? "";
    public bool Exists { get; }

    public string DurationText =>
        Entry.DurationSeconds is { } d ? TimeFormat.Short(TimeSpan.FromSeconds(d)) : "–";

    public string PositionText =>
        Entry.PositionSeconds > 0 ? TimeFormat.Short(TimeSpan.FromSeconds(Entry.PositionSeconds)) : "";

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            OnPropertyChanged();
        }
    }

    public void SetPosition(TimeSpan position)
    {
        Entry.PositionSeconds = Math.Round(position.TotalSeconds, 1);
        OnPropertyChanged(nameof(PositionText));
    }

    public void SetDuration(TimeSpan duration)
    {
        Entry.DurationSeconds = Math.Round(duration.TotalSeconds, 1);
        OnPropertyChanged(nameof(DurationText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
