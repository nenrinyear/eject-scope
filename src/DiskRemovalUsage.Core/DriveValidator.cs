namespace DiskRemovalUsage.Core;

public static class DriveValidator
{
    public static string NormalizeRemovableDrive(string drive)
    {
        if (string.IsNullOrWhiteSpace(drive)) throw new ArgumentException("ドライブを指定してください。", nameof(drive));
        var root = Path.GetPathRoot(drive.Trim());
        if (root is null || root.Length != 3 || root[1] != ':')
            throw new ArgumentException("E:\\ のようなドライブレターを指定してください。", nameof(drive));

        var info = new DriveInfo(root);
        if (!info.IsReady) throw new IOException("対象ドライブにアクセスできません。");
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.Equals(info.RootDirectory.FullName, systemRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Windows が起動しているシステムドライブは対象にできません。", nameof(drive));
        if (info.DriveType is not (DriveType.Removable or DriveType.Fixed))
            throw new ArgumentException("ローカルのリムーバブルまたは外付けドライブを指定してください。", nameof(drive));
        return info.RootDirectory.FullName;
    }
}
