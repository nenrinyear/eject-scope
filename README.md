# Disk Removal Usage

A Windows tray application that helps identify processes preventing safe removal of a USB drive or external storage device.

> The app diagnoses likely blockers; it does not force-eject a device. Use Windows' standard safe-removal flow after resolving the cause.

## Features

- Lists local removable and external drives, excluding the Windows system drive and network drives.
- Detects processes holding file handles on the selected drive when run as administrator.
- Shows process name, PID, executable path, locked path, risk level, and a recommended action.
- Opens the scanner when Windows reports a failed removal request, and can optionally scan automatically.
- Requires explicit confirmation before terminating a user-selected process; system processes cannot be terminated from the app.
- Runs from the notification area with startup and elevation options.

## Requirements

- Windows 10 or later
- .NET SDK 10.0.301 or a compatible later patch version for development
- Administrator rights for system-wide file-handle detection

## Build and run

```powershell
dotnet restore DiskRemovalUsage.sln
dotnet build DiskRemovalUsage.sln --configuration Release --no-restore
dotnet run --project src/DiskRemovalUsage.App
```

## How detection works

Windows Restart Manager accepts file paths, not a whole drive directory. For drive-level detection, this app duplicates file handles from running processes and resolves their final paths. That requires elevation to inspect handles owned by other processes.

Without elevation, the app can still open itself after a failed removal notification and guide you to restart as administrator, but it cannot guarantee a complete blocker list.

## Safety and privacy

- No telemetry or network communication is implemented.
- Settings are stored locally under the current user's Local AppData directory.
- Scan results can contain sensitive file paths. Review and redact them before filing an issue or sharing a screenshot.
- The app never forces removal and never terminates a process without a confirmation dialog.

## Manual verification

1. Open a file on a USB drive in Notepad, then scan the drive as administrator.
2. Open the drive in Explorer and verify that it is shown as a caution-level process if detected.
3. Trigger a failed safe-removal attempt and verify that the scanner window opens with the corresponding drive selected.
4. Confirm that cancelling the termination dialog leaves the process running.
5. Confirm that system processes cannot be terminated from the app.

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md).

## License

This project is licensed under the [MIT License](LICENSE).
