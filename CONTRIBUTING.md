# Contributing

Thanks for considering a contribution.

## Development setup

- Windows 10 or later
- .NET SDK version specified in `global.json`

```powershell
dotnet restore DiskRemovalUsage.sln
dotnet build DiskRemovalUsage.sln --configuration Release --no-restore
```

## Pull requests

- Keep a pull request focused on one change.
- Explain how the change was manually verified.
- Do not add telemetry, automatic process termination, or kernel drivers without prior discussion.
- Do not commit device labels, file paths, screenshots, logs, or other data from another person's computer.

## Safety expectations

This app examines process handles and can offer to terminate a selected process. Changes must preserve explicit confirmation before termination and must never force device removal.
