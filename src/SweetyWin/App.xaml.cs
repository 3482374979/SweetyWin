using System;
using System.Threading;
using System.Windows;
using SweetyWin.Native;
using SweetyWin.Services;
using SweetyWin.Views;

namespace SweetyWin;

/// <summary>
/// 애플리케이션 진입점. 단일 인스턴스 보장 + 호스트 윈도우 + 글로벌 핫키 + 트레이.
/// macOS Sweety 의 AppDelegate + TextActionService.show 의 진입 흐름에 대응.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\SweetyWin.SingleInstance";
    private Mutex? _singleInstanceMutex;

    private HotkeyService? _hotkeyService;
    private QuickPopWindow? _quickPop;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 단일 인스턴스 — 두 번째 실행은 즉시 종료
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out var created);
        if (!created)
        {
            MessageBox.Show("SweetyWin is already running.", "SweetyWin",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // QuickPop 호스트 윈도우 — 숨겨진 상태로 유지, 핫키/선택 감지 시 표시
        _quickPop = new QuickPopWindow();
        // 핫키: Ctrl+Shift+Space → QuickPop 토글 (Phase 2 에서 텍스트 선택 트리거로 교체 예정)
        _hotkeyService = new HotkeyService();
        var id = _hotkeyService.Register(
            HotkeyModifiers.Control | HotkeyModifiers.Shift,
            VirtualKey.Space,
            () => _quickPop.ToggleNearCursor());
        if (id < 0)
        {
            MessageBox.Show("Failed to register hotkey Ctrl+Shift+Space — another app may be using it.",
                "SweetyWin", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _quickPop?.Close();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
