// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows;
using System.Windows.Input;

namespace Blink.App.Update;

/// <summary>업데이트 후 첫 실행에서 방금 설치된 버전의 릴리스 노트를 보여준다.</summary>
public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow(string version, string releaseNotesMarkdown)
    {
        InitializeComponent();
        HeaderText.Text = $"Blink v{version}으로 업데이트되었습니다";
        MarkdownView.Render(NotesPanel, releaseNotesMarkdown);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Donate_Click(object sender, RoutedEventArgs e)
    {
        var win = new DonateWindow { Owner = this };
        win.Show();
        win.Activate();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
