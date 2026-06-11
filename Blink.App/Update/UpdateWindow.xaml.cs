// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Blink.Core.Config;
using Blink.Core.Update;

namespace Blink.App.Update;

/// <summary>
/// "새 버전" 안내 창: 릴리스 노트 + [지금 업데이트 / 나중에 / 이 버전 건너뛰기].
/// 지금 업데이트는 다운로드 진행률을 창 안에 표시하고, 완료되면 silent 인스톨러를
/// 실행한 뒤 앱을 종료한다(인스톨러가 exe를 교체할 수 있게 파일 잠금 해제).
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly ReleaseInfo _release;
    private readonly AppConfig _config;
    private readonly Action _quitForUpdate;
    private CancellationTokenSource? _cts;

    public UpdateWindow(ReleaseInfo release, AppConfig config, Action quitForUpdate)
    {
        InitializeComponent();
        _release = release;
        _config = config;
        _quitForUpdate = quitForUpdate;

        VersionText.Text = $"v{UpdateService.CurrentVersion}  →  v{release.Version}";
        MarkdownView.Render(NotesPanel, release.Body);
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        UpdateBtn.IsEnabled = false;
        SkipBtn.IsEnabled = false;
        LaterBtn.IsEnabled = false;
        ProgressArea.Visibility = Visibility.Visible;
        CancelBtn.IsEnabled = true;
        DownloadBar.Value = 0;
        StatusText.Text = "다운로드 중…";
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(p => DownloadBar.Value = p);
            var path = await UpdateService.DownloadInstallerAsync(_release, progress, _cts.Token);

            StatusText.Text = "설치를 시작합니다 — 앱이 잠시 종료됩니다…";
            CancelBtn.IsEnabled = false;
            UpdateService.LaunchInstaller(path);
            _quitForUpdate();
        }
        catch (OperationCanceledException)
        {
            ResetAfterFailure("다운로드를 취소했습니다.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Blink] update download failed: {ex}");
            ResetAfterFailure("다운로드에 실패했습니다. 네트워크 상태를 확인한 뒤 다시 시도해 주세요.");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ResetAfterFailure(string message)
    {
        StatusText.Text = message;
        DownloadBar.Value = 0;
        UpdateBtn.Content = "다시 시도";
        UpdateBtn.IsEnabled = true;
        SkipBtn.IsEnabled = true;
        LaterBtn.IsEnabled = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        // 이 버전은 다시 알리지 않되, 더 새로운 버전이 나오면 다시 알린다 (UpdatePolicy).
        _config.SkipVersion = _release.Version.ToString();
        _config.Save();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel(); // 창을 닫으면 진행 중인 다운로드도 중단
        base.OnClosed(e);
    }
}
