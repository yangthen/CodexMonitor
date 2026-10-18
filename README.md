# Codex Monitor

[English](README.md) · [简体中文](README.zh-CN.md)

A Windows floating window for Codex usage. It stays in the system tray when you do not need it.

![Codex Monitor floating window illustration with sample data](docs/hero.png)

## What it shows

- **Quota:** 5-hour and weekly usage, progress bars, and reset times.
- **Output speed:** estimated tokens/s from local Codex sessions, with a smooth 5-minute chart. Open the detail view for 15 minutes, 1 hour, 24 hours, 3 days, or 7 days.
- **A window that fits your desktop:** drag to move, resize from the edges, and optionally keep it on top. Click the tray icon to show or bring it forward; use the tray menu to hide it.

## Run locally

Requires Windows, .NET 9, the Windows App SDK runtime, and a signed-in Codex CLI or Codex desktop app. Open `CodexMonitor.sln` in Visual Studio and run `CodexMonitor.App`, or run:

```powershell
dotnet run --project .\src\CodexMonitor.App\CodexMonitor.App.csproj
```

If Codex is not on `PATH`, set `CODEX_MONITOR_CODEX_PATH` to the full path of `codex.exe`.

## Data and privacy

Quota comes from the local Codex app-server. Speed is an **estimate** based on output-token changes in `~/.codex/sessions`; it is not a server-side live token stream. Display history and preferences are stored under `%LOCALAPPDATA%\CodexMonitor`. The app does not save prompts or login tokens to its cache. Monitoring runs locally, with no separate network service.
