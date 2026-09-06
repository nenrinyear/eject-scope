# EjectScope

A Windows tray application that helps identify processes preventing safe removal of a USB drive or external storage device.

> The app diagnoses likely blockers; it does not force-eject a device. Use Windows' standard safe-removal flow after resolving the cause.

## Features

- Lists local removable and external drives, excluding the Windows system drive and network drives.
- Detects processes holding file handles on the selected drive when run as administrator.
- Shows scan progress and lets you cancel a long-running handle scan.
- Lists processes whose handles could not be resolved as unconfirmed candidates without enabling termination.
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
dotnet restore EjectScope.sln
dotnet build EjectScope.sln --configuration Release --no-restore
dotnet run --project src/EjectScope.App
```

## Create a release build

For a portable, self-contained Windows x64 executable that does not require the .NET 10 runtime on the destination computer:

```powershell
dotnet publish .\src\EjectScope.App\EjectScope.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  --output .\artifacts\publish\win-x64
```

The distributable executable is created at:

```text
artifacts\publish\win-x64\EjectScope.App.exe
```

You can package it for a GitHub release with:

```powershell
Compress-Archive `
  -Path .\artifacts\publish\win-x64\EjectScope.App.exe `
  -DestinationPath .\artifacts\EjectScope-win-x64.zip `
  -Force
```

To create a smaller framework-dependent release instead, use `--self-contained false` and distribute the entire publish directory. Users of that build must install the .NET 10 Desktop Runtime.

Trimming is intentionally not enabled because reflection-heavy Windows desktop frameworks can be broken by trimming. Release executables are also not Authenticode-signed by the project, so Windows SmartScreen may warn users about downloaded builds.

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
