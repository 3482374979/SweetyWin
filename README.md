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

**자동 트리거 (v0.1.3+)**: 텍스트를 **드래그-선택** 하거나 **더블클릭** 으로 단어 선택 후 마우스 떼면 자동으로 팝업이 떠오릅니다. 팝업 밖을 클릭하면 자동으로 닫힙니다.

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

## 진단 (동작 안 함 신고 시)

설정 → "로그 파일 열기" → `%LOCALAPPDATA%\SweetyWin\sweetywin.log` 내용 공유.
- 마우스 후킹 설치 여부
- 핫키 등록 여부
- 캡처 방법 (UIA vs Ctrl+C) 및 결과
- 번역 시도 결과 (provider, 길이, 에러)

특정 앱에서 안 될 때 어떤 앱인지(앱 이름·버전) 함께 공유 부탁드립니다.

## 호환성 노트

캡처 경로 (v0.1.3 기준, 순차 시도):
1. **UIA TextPattern** — focused element 자신부터 ancestor 5단계까지 탐색
2. **UIA LegacyIAccessiblePattern** (MSAA fallback) — win32 legacy 앱용
3. **Ctrl+C → 클립보드** — 위 둘 실패 시 (1000ms 타임아웃)

- **관리자 권한으로 실행되는 앱** → 일반 권한 SweetyWin 이 마우스/키 후킹 못 함 (UIPI). SweetyWin 도 관리자 권한 실행 필요.
- **Electron 앱(Slack/Discord/VSCode)** → UIA 잘 동작
- **한컴오피스 한글/카카오톡** → Ctrl+C fallback 또는 LegacyPattern

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
