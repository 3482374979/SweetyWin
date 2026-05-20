using System;
using System.Net.Http;
using System.Threading;
using System.Windows;
using SweetyWin.Native;
using SweetyWin.Services;
using SweetyWin.Translation;
using SweetyWin.Views;

namespace SweetyWin;

/// <summary>
/// 애플리케이션 진입점. 단일 인스턴스 + 서비스 와이어업 + 글로벌 핫키 + 트레이 + 호스트 윈도우.
/// macOS Sweety 의 AppDelegate + TextActionService.show 진입 흐름에 대응.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\SweetyWin.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    private SettingsService? _settings;
    private SelectionService? _selection;
    private TranslationService? _translation;
    private HotkeyService? _hotkeyService;
    private TrayIconService? _tray;
    private HttpClient? _http;
    private QuickPopWindow? _quickPop;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── 단일 인스턴스 ────────────────────────────────────────
        _singleInstanceMutex = new Mutex(initiallyOwned: false, name: SingleInstanceMutexName, createdNew: out var created);
        if (!created)
        {
            MessageBox.Show("SweetyWin is already running.", "SweetyWin",
                MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }
        try
        {
            _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsSingleInstanceMutex = true;
        }

        // ── 서비스 와이어업 ──────────────────────────────────────
        _settings = new SettingsService();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _selection = new SelectionService();
        _translation = new TranslationService(_settings, _http);

        // ── QuickPop 윈도우 (숨김 유지) ───────────────────────────
        _quickPop = new QuickPopWindow(_selection, _translation);

        // ── 트레이 아이콘 ─────────────────────────────────────────
        _tray = new TrayIconService(
            onToggle: () => _quickPop?.ToggleNearCursor(),
            onSettings: ShowSettingsWindow,
            onQuit: () => Shutdown());

        // ── 글로벌 핫키 ──────────────────────────────────────────
        _hotkeyService = new HotkeyService();
        RegisterHotkeyFromSettings();
    }

    /// <summary>설정 기반 핫키 등록 — 설정 변경 후 재등록 가능.</summary>
    private void RegisterHotkeyFromSettings()
    {
        if (_hotkeyService == null || _settings == null) return;
        var s = _settings.Current;
        var id = _hotkeyService.Register(
            (HotkeyModifiers)s.HotkeyModifiers,
            (VirtualKey)s.HotkeyVk,
            () => _quickPop?.ToggleNearCursor());
        if (id < 0)
        {
            MessageBox.Show(
                "Failed to register global hotkey — another app may be using it.\n" +
                "기본값: Ctrl+Shift+Space. 설정에서 settings.json 으로 변경 가능.",
                "SweetyWin", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>설정창 표시 — 중복 열기 방지.</summary>
    private void ShowSettingsWindow()
    {
        if (_settings == null) return;
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings);
        var result = _settingsWindow.ShowDialog();
        _settingsWindow = null;
        // 저장 시 핫키 재등록 (단축키 변경 가능성 대비) — 향후 hotkey 편집 UI 추가 시 필요
        // 현재는 settings.json 직접 편집 후 앱 재시작이 권장 경로.
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _tray?.Dispose();
        _quickPop?.Close();
        _settingsWindow?.Close();
        _http?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); }
            catch (ApplicationException) { /* not owner */ }
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
