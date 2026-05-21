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

**기본 트리거 (v0.2.1+)**: `Ctrl+Shift+Space` 핫키로 현재 선택 텍스트 캡처 → 팝업. 모든 환경에서 안정.

**옵트인 자동 트리거**: 설정에서 활성화 시, 텍스트 **드래그-선택** / **더블클릭** 으로도 자동 팝업. 글로벌 마우스 후킹 사용 — 일부 환경에서 마우스 응답성에 영향 가능.

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

**v0.1.4+**: 진단 로그는 기본 **OFF** (성능 최적화).

신고 시 절차:
1. 설정 → "**진단 로그 활성화**" 체크 → 저장
2. 문제 재현 (드래그-선택, 어떤 앱에서, 무엇이 안 됨)
3. "로그 파일 열기" → 마지막 ~30줄 공유
4. 끝나면 체크 해제 (성능 복귀)

에러/실패(핫키 등록 실패, 마우스 후킹 실패, 번역 API 에러)는 진단 로그 OFF 여도 항상 기록됩니다.

## 호환성 노트

**안정성 정책 (v0.2.1+)** — 기본 모드는 **핫키 전용**(`Ctrl+Shift+Space`):
- 글로벌 마우스 후킹 없음 → 모든 환경에서 안정 (백신·다른 후킹 도구·저사양 PC 모두 안전)
- 드래그/더블클릭 자동 표시는 **옵트인** — 설정에서 활성화 시만 `WH_MOUSE_LL` 사용
- 후킹 사용 시 일부 환경에서 마우스 응답성/안정성 영향 가능 — 문제 시 체크 해제로 즉시 복귀

**안정성 (v0.1.5+)**: WH_MOUSE_LL 후킹(활성 시)을 **전용 BG 스레드**(AboveNormal priority) 에서 실행. UI 가 무거운 작업(번역 API 호출, 렌더링) 중이어도 마우스 이벤트는 지연 없이 처리. 전역 예외 핸들러로 어떤 비정상 상황에서도 강제 종료 방지.

**자동시작 & 리소스 최적화 (v0.2.0+)**:
- HKCU\Run 경로 자가 치유 — exe 이동해도 다음 실행에서 registry 자동 갱신
- 시작 시 silent — 로그인 시 MessageBox 차단 없음, 트레이 툴팁에 상태 표시
- SetWindowsHookEx 3회 재시도(500ms 백오프) — AV 일시 차단 대응
- explorer.exe 재시작 시 트레이 자동 부활 — TaskbarCreated 메시지 핸들
- 번역 LRU 캐시 (50 entries) — 같은 텍스트 반복 시 네트워크 0회
- 설정 파일 원자적 쓰기 (.tmp → rename) — 충돌 시 손상 방지
- 로그 파일 자동 회전 (1MB) — 매 100건마다 체크
- 클릭아웃 dispatch 50ms throttle — 빠른 클릭 큐 적체 방지
- AccessViolationException 명시 catch — UIA 호출 안전성

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
