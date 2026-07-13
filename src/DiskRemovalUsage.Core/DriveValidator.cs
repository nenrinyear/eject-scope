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
        if (info.DriveType != DriveType.Removable)
            throw new ArgumentException("リムーバブルドライブのみを対象にできます。", nameof(drive));
        return info.RootDirectory.FullName;
    }
}
