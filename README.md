# Paint.NET Auto-Save

An in-process Paint.NET 5.2 effect plugin targeting .NET 11. It provides
configurable History-based and time-based recovery snapshots without modifying
the live document.

## AI Disclosure

This project was written by ChatGPT via Codex desktop app. It was tested (by human) to make sure it works fine.

## Current behavior

- Each open document has its own History counter and time-save countdown.
- Snapshots preserve the native Paint.NET document structure through
  `Document.Clone()` and `Document.SaveToStream(Stream)`.
- Saved documents use names such as `image_1-53-30_16.11.2026.pdn`.
- Unsaved documents use names such as `snapshot_1-53-30_16.11.2026.pdn`.
- Snapshots are written to
  `%LOCALAPPDATA%\Paint.NET\AutoSavePdnPoc\snapshots`.
- Settings are stored in
  `%LOCALAPPDATA%\Paint.NET\AutoSavePdnPoc\settings.json`.

To enable the plugin, navigate to "Effects -> Auto-Save" and click on the only option there. You will be able to configure some config values and enable the plugin.
It needs to be manually enabled each time you restart Paint.NET, it is **not** enabled by itself (and probably there is no way to make it so).

The Paint.NET status bar shows the active document’s state, for example
`Auto-Save: 3/10, 4:52`.

## Build

### Install .NET 11 SDK

The project requires the .NET 11 SDK, not only the runtime. On Windows 11,
open PowerShell or Command Prompt and run:

```powershell
winget install Microsoft.DotNet.SDK.11
```

Close and reopen the terminal, then verify the installation:

```powershell
dotnet --list-sdks
dotnet --version
```

The output should include an `11.0.x` SDK. If `winget` is unavailable, use
the official [.NET 11 download page](https://dotnet.microsoft.com/download/dotnet/11.0)
and install the Windows x64 SDK.

### Build the plugin

```powershell
dotnet build .\AutoSavePdnPoc\AutoSavePdnPoc.csproj -c Release
```

The project references the Paint.NET 5.2 assemblies from
`C:\Program Files\paint.net`. Copy the resulting
`AutoSavePdnPoc.dll` to Paint.NET’s Effects directory.

## Compatibility notes

The plugin uses the public modern effect API for the effect itself. Paint.NET
5.2 does not expose an official API for the main-window status indicator or
document-workspace enumeration, so those parts are located at runtime and may
need adjustment for future Paint.NET releases.

## License

MIT. See [LICENSE](LICENSE).

## Paint.NET notice

Paint.NET, PaintDotNet, and related names and marks belong to dotPDN LLC and
their respective copyright holders. This repository does not include or
redistribute Paint.NET binaries, source code, or assets. The project is a
third-party plugin and expects the user to have Paint.NET installed locally;
the project references those local assemblies only for building and loading
the plugin.
