using Microsoft.Win32;

namespace EjectScope.App;

internal static class StartupRegistration
{
    private const string ValueName = "EjectScope";
    private const string LegacyValueName = "DiskRemovalUsage";
    private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? throw new InvalidOperationException("スタートアップ設定を開けません。");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");
            key.SetValue(ValueName, $"\"{executable}\"");
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
    }
}
