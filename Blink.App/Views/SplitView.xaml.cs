// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows.Controls;
using System.Windows.Input;
using Blink.App.ViewModels;

namespace Blink.App.Views;

public partial class SplitView : UserControl
{
    /// <summary>Raised when the user requests an action: "open", "copy", or "reveal".</summary>
    public event Action<string>? ActionRequested;

    public SplitView() => InitializeComponent();

    private LauncherViewModel? Vm => DataContext as LauncherViewModel;

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

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem != null)
            List.ScrollIntoView(List.SelectedItem);
    }

    private void Open_Click(object sender, System.Windows.RoutedEventArgs e) => ActionRequested?.Invoke("open");
    private void Copy_Click(object sender, System.Windows.RoutedEventArgs e) => ActionRequested?.Invoke("copy");
    private void Reveal_Click(object sender, System.Windows.RoutedEventArgs e) => ActionRequested?.Invoke("reveal");
}
