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
/// 애플리케이션 진입점. 단일 인스턴스 + 서비스 와이어업 + 글로벌 핫키 + 호스트 윈도우.
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
    private HttpClient? _http;
    private QuickPopWindow? _quickPop;

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

        // ── 호스트 윈도우 (숨김 상태로 유지, 핫키 시 표시) ──────
        _quickPop = new QuickPopWindow(_selection, _translation);

        // ── 글로벌 핫키 ──────────────────────────────────────────
        _hotkeyService = new HotkeyService();
        var s = _settings.Current;
        var id = _hotkeyService.Register(
            (HotkeyModifiers)s.HotkeyModifiers,
            (VirtualKey)s.HotkeyVk,
            () => _quickPop?.ToggleNearCursor());
        if (id < 0)
        {
            MessageBox.Show(
                "Failed to register global hotkey — another app may be using it.\n" +
                "Default: Ctrl+Shift+Space",
                "SweetyWin", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _quickPop?.Close();
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
