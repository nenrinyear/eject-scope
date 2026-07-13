using System.Windows;
using System.Windows.Controls;

namespace DiskRemovalUsage.App;

public sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly CheckBox _startup;
    private readonly CheckBox _elevate;
    private readonly CheckBox _autoScan;
    private readonly CheckBox _terminate;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "設定"; Width = 390; Height = 250; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _startup = Check("起動時に常駐する", settings.RunAtStartup);
        _elevate = Check("管理者権限で再起動するボタンを表示", settings.ShowElevateButton);
        _autoScan = Check("取り外し失敗時に自動スキャンする", settings.AutoScanOnRemovalFailure);
        _terminate = Check("プロセス終了の提案を有効にする", settings.EnableTerminateSuggestion);
        var save = new Button { Content = "保存", IsDefault = true, Width = 80, Margin = new Thickness(0, 12, 8, 0) };
        save.Click += (_, _) => { settings.RunAtStartup = _startup.IsChecked == true; settings.ShowElevateButton = _elevate.IsChecked == true; settings.AutoScanOnRemovalFailure = _autoScan.IsChecked == true; settings.EnableTerminateSuggestion = _terminate.IsChecked == true; DialogResult = true; };
        var cancel = new Button { Content = "キャンセル", IsCancel = true, Width = 80, Margin = new Thickness(0, 12, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; buttons.Children.Add(save); buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(18) }; panel.Children.Add(_startup); panel.Children.Add(_elevate); panel.Children.Add(_autoScan); panel.Children.Add(_terminate); panel.Children.Add(buttons); Content = panel;
    }
    private static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
}
