// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Blink.App.Mvvm;

/// <summary>Minimal INotifyPropertyChanged base for the launcher view-models.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}
