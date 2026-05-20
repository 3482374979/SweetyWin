# SweetyWin

Windows port of [Sweety](https://github.com/3482374979/Sweety) QuickPop — text selection action palette with translation focus.

> macOS 의 Sweety 퀵팝(텍스트 선택 시 떠오르는 액션 팔레트) 의 Windows 포팅. 번역 기능 우선.

## Tech Stack

- **.NET 8 WPF** (C#)
- Win32 API (P/Invoke) — global hotkey, nonactivating popup, cursor position
- UI Automation (planned, Phase 2) — selection text detection
- Translation APIs (planned, Phase 3) — DeepL Free / Naver Papago

## Status

- [x] **Phase 1** — Project scaffold, single-instance, global hotkey, popup window
- [x] **Phase 2** — SelectionService (UI Automation + Ctrl+C clipboard fallback)
- [x] **Phase 3** — Translation pipeline (MyMemory default + DeepL when API key set), inline result panel with auto language detection
- [x] **Phase 4** — System tray icon, auto-start (HKCU\Run), settings window
- [ ] **Phase 5** — Hotkey recorder UI, clipboard history (optional)

## How it works

**자동 트리거 (v0.1.1+)**: 텍스트를 드래그-선택하고 마우스 떼면 자동으로 팝업이 떠오릅니다. 설정에서 끌 수 있습니다.

**수동 트리거**: `Ctrl+Shift+Space` 누르면 현재 선택 텍스트로 팝업.

1. Drag-select text **OR** press `Ctrl+Shift+Space` after selecting
2. SelectionService captures the selection via UIA (no clipboard pollution); falls back to Ctrl+C + clipboard read/restore for non-UIA apps
3. QuickPop appears near cursor with action buttons
4. Click **번역** → result panel expands inline; auto-detects source language (Hangul/Kana/Han/Latin/Cyrillic heuristic) and infers target (Korean → English, else → Korean)
5. Translation routes through providers in priority order:
   - **DeepL Free** (if `DeepLApiKey` set in settings.json)
   - **MyMemory** (default, no API key needed, ~1000 words/day)

## Settings

Tray icon → 우클릭 → **설정...** 또는 `%LOCALAPPDATA%\SweetyWin\settings.json` 직접 편집.

```json
{
  "DeepLApiKey": "your-key:fx",
  "HotkeyVk": 32,
  "HotkeyModifiers": 6
}
```

- `HotkeyModifiers`: `2`=Ctrl, `4`=Shift, `1`=Alt, `8`=Win (combinable)
- `HotkeyVk`: Win32 VK code (space = `0x20`)
- 설정창에서 DeepL 키 + 자동 시작 토글 가능. 핫키 변경은 settings.json 편집 후 앱 재시작.

## Tray

- 좌클릭 → QuickPop 토글 (핫키와 동일)
- 우클릭 → 메뉴 (열기 / 설정 / 종료)

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
