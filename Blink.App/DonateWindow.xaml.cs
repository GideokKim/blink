// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows;
using System.Windows.Input;

namespace Blink.App;

/// <summary>트레이 메뉴와 "새로워진 점" 창에서 여는 후원 안내 창.</summary>
public partial class DonateWindow : Window
{
    public DonateWindow() => InitializeComponent();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
