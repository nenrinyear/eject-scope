using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DiskRemovalUsage.Core;
using DiskRemovalUsage.Core.Models;

namespace DiskRemovalUsage.App;

public sealed class MainWindow : Window
{
    private readonly ComboBox _drives = new() { MinWidth = 130, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _scan = new() { Content = "スキャン", Padding = new Thickness(14, 4, 14, 4) };
    private readonly Button _elevate = new() { Content = "管理者として再起動", Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _terminate = new() { Content = "選択したプロセスを終了", IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 10, 0, 6), TextWrapping = TextWrapping.Wrap };
    private readonly ObservableCollection<ProcessUsage> _items = [];
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, MinHeight = 250 };
    private readonly RestartManagerScanner _scanner = new();
    private readonly AppSettings _settings = AppSettings.Load();

    public bool AllowClose { get; set; }

    public MainWindow()
    {
        Title = "ディスク取り外しブロッカー解析";
        Width = 980;
        Height = 520;
        MinWidth = 720;
        Content = BuildContent();
        PopulateDrives();
        _scan.Click += async (_, _) => await ScanAsync();
        _elevate.Click += (_, _) => RestartElevated();
        _terminate.Click += async (_, _) => await TerminateSelectedAsync();
        _grid.SelectionChanged += (_, _) => _terminate.IsEnabled = _settings.EnableTerminateSuggestion && (_grid.SelectedItem as ProcessUsage)?.CanTerminate == true;
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
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
        _elevate.Visibility = _settings.ShowElevateButton ? Visibility.Visible : Visibility.Collapsed;
        controls.Children.Add(_elevate);
        controls.Children.Add(_terminate);
        var panel = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(controls, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        panel.Children.Add(controls);
        panel.Children.Add(_status);
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
        var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).Select(d => d.RootDirectory.FullName).ToArray();
        _drives.ItemsSource = drives;
        _drives.SelectedIndex = drives.Length > 0 ? 0 : -1;
        _status.Text = drives.Length == 0 ? "接続されているリムーバブルドライブがありません。" : "ドライブを選んでスキャンしてください。";
    }

    public async Task ScanAsync()
    {
        if (_drives.SelectedItem is not string drive)
        {
            PopulateDrives();
            return;
        }
        try
        {
            _scan.IsEnabled = false;
            _status.Text = $"{drive} をスキャンしています…";
            var result = await _scanner.ScanAsync(drive);
            _items.Clear();
            foreach (var item in result.Processes) _items.Add(item);
            _status.Text = result.Processes.Count == 0 ? $"{drive} を使用中のプロセスは Restart Manager では検出されませんでした。" : $"{result.Processes.Count} 件検出。{result.Detail}";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or Win32Exception)
        {
            _status.Text = ex.Message;
        }
        finally { _scan.IsEnabled = true; }
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
