# Paint.NET Auto-Save

An in-process Paint.NET 5.2 effect plugin targeting .NET 11. It provides
configurable History-based and time-based recovery snapshots without modifying
the live document.

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

The plugin activates only after the user clicks **Save config and enable**.
The Paint.NET status bar shows the active document’s state, for example
`Auto-Save: 3/10, 4:52`.

## Build

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
