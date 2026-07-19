using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using EjectScope.Core;
using EjectScope.Core.Models;

namespace EjectScope.App;

public sealed class MainWindow : Window
{
    private readonly ComboBox _drives = new() { MinWidth = 130, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _scan = new() { Content = "スキャン", Padding = new Thickness(14, 4, 14, 4) };
    private readonly Button _cancelScan = new() { Content = "キャンセル", IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _elevate = new() { Content = "管理者として再起動", Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _terminate = new() { Content = "選択したプロセスを終了", IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 10, 0, 6), TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Height = 14, Minimum = 0, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ObservableCollection<ProcessUsage> _items = [];
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, MinHeight = 250 };
    private readonly RestartManagerScanner _scanner = new(Environment.ProcessPath);
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Dictionary<string, DateTime> _recentRemovalFailures = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _scanCancellation;

    public bool AllowClose { get; set; }

    public MainWindow()
    {
        Title = "EjectScope — 取り外しブロッカー解析";
        Width = 980;
        Height = 520;
        MinWidth = 720;
        Content = BuildContent();
        PopulateDrives();
        _scan.Click += async (_, _) => await ScanAsync();
        _cancelScan.Click += (_, _) => _scanCancellation?.Cancel();
        _elevate.Click += (_, _) => RestartElevated();
        _terminate.Click += async (_, _) => await TerminateSelectedAsync();
        _grid.SelectionChanged += (_, _) => _terminate.IsEnabled = _settings.EnableTerminateSuggestion && (_grid.SelectedItem as ProcessUsage)?.CanTerminate == true;
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
        SourceInitialized += (_, _) => (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WndProc);
    }

    private UIElement BuildContent()
    {
        _grid.Columns.Add(new DataGridTextColumn { Header = "プロセス名", Binding = new System.Windows.Data.Binding(nameof(ProcessUsage.ProcessName)), Width = 130 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "PID", Binding = new System.Windows.Data.Binding(nameof(ProcessUsage.ProcessId)), Width = 70 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "実行ファイル", Binding = new System.Windows.Data.Binding(nameof(ProcessUsage.ExecutablePath)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "使用中の範囲", Binding = new System.Windows.Data.Binding("LockedPaths[0]"), Width = 100 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "目安", Binding = new System.Windows.Data.Binding(nameof(ProcessUsage.Risk)), Width = 100 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "推奨操作", Binding = new System.Windows.Data.Binding(nameof(ProcessUsage.Recommendation)), Width = 230 });
        _grid.ItemsSource = _items;

        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(new TextBlock { Text = "対象ドライブ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        controls.Children.Add(_drives);
        controls.Children.Add(_scan);
        controls.Children.Add(_cancelScan);
        _elevate.Visibility = _settings.ShowElevateButton ? Visibility.Visible : Visibility.Collapsed;
        controls.Children.Add(_elevate);
        controls.Children.Add(_terminate);
        var panel = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(controls, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        DockPanel.SetDock(_progress, Dock.Top);
        panel.Children.Add(controls);
        panel.Children.Add(_status);
        panel.Children.Add(_progress);
        panel.Children.Add(_grid);
        return panel;
    }

    private void RestartElevated()
    {
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" });
            AllowClose = true;
            Close();
        }
        catch (System.ComponentModel.Win32Exception) { /* UAC のキャンセル */ }
    }

    private void PopulateDrives()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Where(d => d.DriveType is DriveType.Removable or DriveType.Fixed)
            .Where(d => !string.Equals(d.RootDirectory.FullName, systemRoot, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.RootDirectory.FullName)
            .ToArray();
        _drives.ItemsSource = drives;
        _drives.SelectedIndex = drives.Length > 0 ? 0 : -1;
        _status.Text = drives.Length == 0
            ? "対象にできるローカルドライブがありません。ドライブを接続後、［スキャン］を押して再読み込みしてください。"
            : "ドライブを選んでスキャンしてください。USB 外付けドライブは Windows 上で「固定」と表示される場合も候補に含めています。";
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == DeviceChange.Message && wParam.ToInt64() == DeviceChange.QueryRemoveFailed && DeviceChange.TryGetDriveRoot(lParam, out var drive))
        {
            Dispatcher.BeginInvoke(async () => await HandleRemovalFailureAsync(drive));
        }
        return IntPtr.Zero;
    }

    private async Task HandleRemovalFailureAsync(string drive)
    {
        var now = DateTime.UtcNow;
        if (_recentRemovalFailures.TryGetValue(drive, out var lastSeen) && now - lastSeen < TimeSpan.FromSeconds(3)) return;
        _recentRemovalFailures[drive] = now;

        PopulateDrives();
        if (_drives.Items.Cast<string>().Contains(drive, StringComparer.OrdinalIgnoreCase)) _drives.SelectedItem = drive;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _status.Text = $"{drive} の安全な取り外しが失敗しました。使用中のプロセスを確認してください。";
        if (_settings.AutoScanOnRemovalFailure && _drives.SelectedItem is not null) await ScanAsync();
    }

    public async Task ScanAsync()
    {
        if (_scanCancellation is not null) return;
        if (_drives.SelectedItem is not string drive)
        {
            PopulateDrives();
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        try
        {
            _scan.IsEnabled = false;
            _cancelScan.IsEnabled = true;
            _progress.IsIndeterminate = true;
            _progress.Visibility = Visibility.Visible;
            _status.Text = $"{drive} をスキャンしています…";
            var progress = new Progress<ScanProgress>(value => UpdateScanProgress(drive, cancellation, value));
            var result = await _scanner.ScanAsync(drive, progress, cancellation.Token);
            _items.Clear();
            foreach (var item in result.Processes) _items.Add(item);
            _status.Text = result.Processes.Count == 0 ? result.Detail : $"{result.Processes.Count} 件検出。{result.Detail}";
        }
        catch (OperationCanceledException) { _status.Text = "スキャンをキャンセルしました。"; }
        catch (Exception ex) when (ex is ArgumentException or IOException or Win32Exception)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _scanCancellation = null;
            _scan.IsEnabled = true;
            _cancelScan.IsEnabled = false;
            _progress.Visibility = Visibility.Collapsed;
            _progress.IsIndeterminate = false;
        }
    }

    private void UpdateScanProgress(string drive, CancellationTokenSource cancellation, ScanProgress progress)
    {
        if (_scanCancellation != cancellation) return;
        _progress.IsIndeterminate = progress.Total is null;
        if (progress.Total is long total)
        {
            _progress.Maximum = Math.Max(total, 1);
            _progress.Value = Math.Min(progress.Completed, total);
            var percent = total == 0 ? 100 : progress.Completed * 100 / total;
            var timeoutText = progress.TimedOutHandles == 0 ? string.Empty : $"、タイムアウト {progress.TimedOutHandles} 件";
            _status.Text = $"{drive} をスキャンしています… {progress.Phase} ({percent}%{timeoutText})";
        }
        else _status.Text = $"{drive} をスキャンしています… {progress.Phase}";
    }

    private async Task TerminateSelectedAsync()
    {
        if (!_settings.EnableTerminateSuggestion || _grid.SelectedItem is not ProcessUsage selected || !selected.CanTerminate) return;
        var answer = MessageBox.Show($"{selected.ProcessName} (PID {selected.ProcessId}) を終了しますか？\n未保存のデータは失われる可能性があります。", "プロセス終了の確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;
        try
        {
            using var process = Process.GetProcessById(selected.ProcessId);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            _status.Text = $"{selected.ProcessName} を終了しました。再スキャンして取り外しを再試行してください。";
            await ScanAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            MessageBox.Show(ex.Message, "終了できません", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ShowSettings()
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                StartupRegistration.SetEnabled(_settings.RunAtStartup);
                _settings.Save();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
            {
                MessageBox.Show(ex.Message, "設定を保存できません", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            _terminate.IsEnabled = _settings.EnableTerminateSuggestion && (_grid.SelectedItem as ProcessUsage)?.CanTerminate == true;
            _elevate.Visibility = _settings.ShowElevateButton ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
