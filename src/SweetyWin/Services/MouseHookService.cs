using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using SweetyWin.Native;

namespace SweetyWin.Services;

/// <summary>
/// 글로벌 마우스 후킹 (WH_MOUSE_LL) — 전용 메시지 루프 스레드 위에서 동작.
///
/// (v0.1.5) 후킹을 UI 스레드에서 분리 — 중요 안정화.
/// 이전(v0.1.4 까지): UI dispatcher 스레드에서 hook 설치 → UI 가 잠시 바쁘면(번역 API,
/// 렌더링) Windows LowLevelHooksTimeout(기본 300ms) 초과 → 모든 마우스 이벤트가
/// 시스템 전체에서 지연되며 lag, 최악의 경우 Windows 가 후킹을 해제하거나 앱을
/// "응답 없음" 으로 강제 종료. 사용자 신고 케이스.
///
/// 수정: 별도 BG 스레드에서 GetMessage 루프 + SetWindowsHookEx 호출. UI 가 무거워도
/// 마우스 이벤트는 hook 스레드에서 즉시 처리됨. 콜백 결과는 UI dispatcher 로 BeginInvoke.
///
/// 두 콜백:
///   1) onSelectionTrigger — 드래그(>5px) OR 더블클릭(500ms 내 동일 위치)
///   2) onLeftClickAnywhere — 모든 LBUTTONDOWN — 클릭아웃 감지(popup hide)용
/// </summary>
public sealed class MouseHookService : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const uint WM_QUIT = 0x0012;
    private const int DragThresholdPx = 5;
    private const int DoubleClickDistPx = 4;
    private static readonly TimeSpan DoubleClickTime = TimeSpan.FromMilliseconds(500);

    private IntPtr _hook;
    private LowLevelMouseProc? _proc;
    private readonly Thread _hookThread;
    private uint _hookThreadId;
    private volatile bool _running = true;

    private readonly Action<User32Interop.POINT> _onSelectionTrigger;
    private readonly Action<User32Interop.POINT> _onLeftClickAnywhere;
    private readonly Dispatcher _uiDispatcher;

    // hook 스레드 전용 상태 — 다른 스레드 접근 금지 (race-free)
    private User32Interop.POINT? _downPoint;
    private DateTime _lastDownTime = DateTime.MinValue;
    private User32Interop.POINT _lastDownPos;
    private bool _pendingDoubleClickUp;

    // suppress — UI 스레드(Suppress 메서드) 와 hook 스레드(콜백) 양쪽 접근. lock 보호.
    private readonly object _suppressLock = new();
    private DateTime _suppressUntil = DateTime.MinValue;

    // (v0.2.0) click-outside dispatch throttle — 50ms 안 연속 클릭 시 BeginInvoke 큐 적체 방지
    private DateTime _lastClickDispatch = DateTime.MinValue;
    private const int ClickDispatchThrottleMs = 50;

    /// <summary>(v0.2.0) hook 설치 성공 여부 — App 이 트레이 툴팁 갱신용으로 조회.</summary>
    public bool IsInstalled => _hook != IntPtr.Zero;

    public MouseHookService(
        Action<User32Interop.POINT> onSelectionTrigger,
        Action<User32Interop.POINT> onLeftClickAnywhere)
    {
        _onSelectionTrigger = onSelectionTrigger;
        _onLeftClickAnywhere = onLeftClickAnywhere;
        _uiDispatcher = System.Windows.Application.Current?.Dispatcher
                        ?? Dispatcher.CurrentDispatcher;

        // (v0.1.5) 전용 BG 스레드 — UI lag 와 무관하게 마우스 이벤트 처리
        _hookThread = new Thread(HookThreadProc)
        {
            IsBackground = true,
            Name = "SweetyWin.MouseHook"
        };
        _hookThread.Start();
    }

    /// <summary>팝업 표시 직후 등 — 짧은 시간 selection trigger 무시.</summary>
    public void Suppress(TimeSpan duration)
    {
        lock (_suppressLock) _suppressUntil = DateTime.UtcNow + duration;
    }

    // ── Hook 스레드 ────────────────────────────────────────────────
    private void HookThreadProc()
    {
        try
        {
            _hookThreadId = GetCurrentThreadId();
            _proc = HookCallback;   // GC 보호: 인스턴스 필드로 보존

            // (v0.2.0) 안티바이러스가 최초 1-2회 차단했다가 허용하는 케이스 — 3회 재시도
            for (int attempt = 0; attempt < 3; attempt++)
            {
                _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
                if (_hook != IntPtr.Zero) break;
                var err = Marshal.GetLastWin32Error();
                LogService.Log($"MouseHook: SetWindowsHookEx attempt {attempt + 1}/3 failed err={err}");
                Thread.Sleep(500);
            }
            if (_hook == IntPtr.Zero)
            {
                LogService.Log("MouseHook: all 3 install attempts failed — fallback to hotkey-only mode");
                return;
            }
            LogService.LogInfo($"MouseHook: installed on bg thread (handle=0x{_hook.ToInt64():X})");

            // 메시지 루프 — hook 콜백이 이 스레드에서 fire 되려면 필수
            while (_running)
            {
                int result = GetMessage(out MSG msg, IntPtr.Zero, 0, 0);
                if (result == 0 || result == -1) break;
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"MouseHook: thread error: {ex.GetType().Name} {ex.Message}");
        }
        finally
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // (v0.1.5) MOUSEMOVE 등 빈번한 이벤트 즉시 통과 — Marshal 비용 회피
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);
        int msg = wParam.ToInt32();
        if (msg != WM_LBUTTONDOWN && msg != WM_LBUTTONUP)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        try
        {
            if (msg == WM_LBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var pt = new User32Interop.POINT { X = data.pt.x, Y = data.pt.y };
                _downPoint = pt;

                // 더블클릭 패턴 감지
                var dt = (DateTime.UtcNow - _lastDownTime).TotalMilliseconds;
                var ddx = Math.Abs(pt.X - _lastDownPos.X);
                var ddy = Math.Abs(pt.Y - _lastDownPos.Y);
                _pendingDoubleClickUp = dt < DoubleClickTime.TotalMilliseconds
                                        && ddx < DoubleClickDistPx
                                        && ddy < DoubleClickDistPx
                                        && _lastDownTime != DateTime.MinValue;
                _lastDownTime = DateTime.UtcNow;
                _lastDownPos = pt;

                // (v0.2.0) throttle — 50ms 안 연속 LBUTTONDOWN 시 dispatch skip (큐 적체 방지)
                var sinceLast = (DateTime.UtcNow - _lastClickDispatch).TotalMilliseconds;
                if (sinceLast >= ClickDispatchThrottleMs)
                {
                    _lastClickDispatch = DateTime.UtcNow;
                    _uiDispatcher.BeginInvoke(new Action(() =>
                    {
                        try { _onLeftClickAnywhere(pt); }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ClickAnywhere handler error: {ex}");
                            LogService.Log($"MouseHook: clickAnywhere handler error: {ex.Message}");
                        }
                    }));
                }
            }
            else // WM_LBUTTONUP
            {
                if (!_downPoint.HasValue) return CallNextHookEx(_hook, nCode, wParam, lParam);
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var up = new User32Interop.POINT { X = data.pt.x, Y = data.pt.y };
                var down = _downPoint.Value;
                _downPoint = null;

                var dx = Math.Abs(up.X - down.X);
                var dy = Math.Abs(up.Y - down.Y);
                var isDrag = (dx > DragThresholdPx || dy > DragThresholdPx);
                var isDoubleClick = _pendingDoubleClickUp;
                _pendingDoubleClickUp = false;

                bool suppressed;
                lock (_suppressLock) suppressed = DateTime.UtcNow < _suppressUntil;
                if (!suppressed && (isDrag || isDoubleClick))
                {
                    var trigger = isDrag ? "drag" : "doubleclick";
                    LogService.LogInfo($"MouseHook: trigger ({trigger}) dx={dx} dy={dy}");
                    _lastDownTime = DateTime.MinValue;  // 트리플클릭 가드
                    _uiDispatcher.BeginInvoke(new Action(() =>
                    {
                        try { _onSelectionTrigger(up); }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"SelectionTrigger handler error: {ex}");
                            LogService.Log($"MouseHook: selection handler error: {ex.Message}");
                        }
                    }));
                }
            }
        }
        catch (Exception ex)
        {
            // hook 콜백에서 예외 절대 throw 안 됨 — Windows hook 해제 가능성
            Debug.WriteLine($"MouseHook callback error: {ex}");
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    // ── P/Invoke ────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    public void Dispose()
    {
        _running = false;
        if (_hookThreadId != 0)
        {
            // 메시지 루프 깨우기 — WM_QUIT 보내 GetMessage return 0
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        _hookThread.Join(TimeSpan.FromSeconds(1));
    }
}
