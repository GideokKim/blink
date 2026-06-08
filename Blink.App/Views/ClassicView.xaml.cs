// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows.Controls;
using System.Windows.Input;
using Blink.App.ViewModels;

namespace Blink.App.Views;

public partial class ClassicView : UserControl
{
    /// <summary>Raised when the user activates a row (click). Argument: "open".</summary>
    public event Action<string>? ActionRequested;

    public ClassicView() => InitializeComponent();

    private LauncherViewModel? Vm => DataContext as LauncherViewModel;

    // Mouse hover shares the keyboard selection index.
    private void Row_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is ListBoxItem item && Vm is { } vm)
        {
            int idx = List.ItemContainerGenerator.IndexFromContainer(item);
            if (idx >= 0) vm.SelectedIndex = idx;
        }
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && Vm is { } vm)
        {
            int idx = List.ItemContainerGenerator.IndexFromContainer(item);
            if (idx >= 0) vm.SelectedIndex = idx;
        }
        ActionRequested?.Invoke("open");
    }

    // Keep the selected row visible as ↑/↓ moves it.
    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem != null)
            List.ScrollIntoView(List.SelectedItem);
    }
}
