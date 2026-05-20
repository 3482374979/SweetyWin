# SweetyWin

Windows port of [Sweety](https://github.com/3482374979/Sweety) QuickPop — text selection action palette with translation focus.

> macOS 의 Sweety 퀵팝(텍스트 선택 시 떠오르는 액션 팔레트) 의 Windows 포팅. 번역 기능 우선.

## Tech Stack

- **.NET 8 WPF** (C#)
- Win32 API (P/Invoke) — global hotkey, nonactivating popup, cursor position
- UI Automation (planned, Phase 2) — selection text detection
- Translation APIs (planned, Phase 3) — DeepL Free / Naver Papago

## Status (Phase 1)

- [x] Project scaffold (.sln, .csproj, app.manifest, single-file publish)
- [x] App.xaml + single instance mutex
- [x] HotkeyService — `Ctrl+Shift+Space` toggles QuickPop near cursor
- [x] QuickPopWindow — nonactivating, topmost, rounded acrylic-ish, cursor-relative position with screen clamp
- [x] Placeholder action buttons (copy / translate / dictionary / search)
- [x] GitHub Actions build → `SweetyWin.exe` single-file artifact

## Roadmap

| Phase | Focus | Status |
|-------|-------|--------|
| **1** | Scaffold + hotkey + popup window | ✅ |
| **2** | Text selection detection (UIA + clipboard fallback) | ⏳ |
| **3** | Translation (DeepL Free / Papago) — primary feature | ⏳ |
| **4** | Settings UI + system tray + auto-start | ⏳ |
| **5** | Clipboard history (optional) | ⏳ |

## Build

CI builds on every push to `main` and on `v*` tags. Local build requires Windows + .NET 8 SDK:

```pwsh
dotnet publish src/SweetyWin/SweetyWin.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o ./publish
```

Output: `./publish/SweetyWin.exe` (~60-80 MB self-contained, no .NET runtime needed on target).

## Release

Tag `vX.Y.Z` push → GitHub Actions builds and attaches `SweetyWin.exe` to a GitHub Release.

```pwsh
git tag -a v0.1.0 -m "Release 0.1.0"
git push origin v0.1.0
```
