using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SweetyWin.Native;

namespace SweetyWin.Services;

/// <summary>
/// 글로벌 마우스 후킹 (WH_MOUSE_LL) — 드래그-선택 종료(LBUTTONUP) 감지.
/// LBUTTONDOWN 위치 ↔ LBUTTONUP 위치 차이가 임계값 이상이면 "드래그" 로 간주.
/// 콜백은 hook 스레드에서 UI 스레드 dispatch — 절대 블록 금지(마우스 lag).
/// macOS Sweety 의 CGEventTap NSLeftMouseUp 감지에 대응.
/// </summary>
public sealed class MouseHookService : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int DragThresholdPx = 5;
    private static readonly TimeSpan PostShowCooldown = TimeSpan.FromMilliseconds(400);

    private const int DoubleClickDistPx = 4;
    private static readonly TimeSpan DoubleClickTime = TimeSpan.FromMilliseconds(500);

    private IntPtr _hook;
    private readonly LowLevelMouseProc _proc;  // GC 보호
    private User32Interop.POINT? _downPoint;
    private readonly Action<User32Interop.POINT> _onSelectionTrigger;
    private readonly Action<User32Interop.POINT> _onLeftClickAnywhere;
    private readonly Dispatcher _dispatcher;
    private DateTime _suppressUntil = DateTime.MinValue;
    private DateTime _lastDownTime = DateTime.MinValue;
    private User32Interop.POINT _lastDownPos;
    private bool _pendingDoubleClickUp;

    public MouseHookService(
        Action<User32Interop.POINT> onSelectionTrigger,
        Action<User32Interop.POINT> onLeftClickAnywhere)
    {
        _onSelectionTrigger = onSelectionTrigger;
        _onLeftClickAnywhere = onLeftClickAnywhere;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _proc = HookCallback;
        // 모듈 핸들은 0 (LL hook 은 모듈 불필요), dwThreadId=0 (모든 스레드)
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"SetWindowsHookEx failed: {err}");
            LogService.Log($"MouseHook: SetWindowsHookEx failed err={err}");
        }
        else
        {
            LogService.Log($"MouseHook: installed (handle=0x{_hook.ToInt64():X})");
        }
    }

    /// <summary>방금 팝업 띄운 직후 등 — 짧은 시간 마우스 hook 동작 무시.</summary>
    public void Suppress(TimeSpan duration)
    {
        _suppressUntil = DateTime.UtcNow + duration;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);

        try
        {
            int msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var pt = new User32Interop.POINT { X = data.pt.x, Y = data.pt.y };
                _downPoint = pt;

                // 더블클릭 패턴 감지 — 이전 down 과 시간/거리 비교 (v0.1.3)
                var dt = (DateTime.UtcNow - _lastDownTime).TotalMilliseconds;
                var ddx = Math.Abs(pt.X - _lastDownPos.X);
                var ddy = Math.Abs(pt.Y - _lastDownPos.Y);
                _pendingDoubleClickUp = dt < DoubleClickTime.TotalMilliseconds
                                        && ddx < DoubleClickDistPx
                                        && ddy < DoubleClickDistPx
                                        && _lastDownTime != DateTime.MinValue;
                _lastDownTime = DateTime.UtcNow;
                _lastDownPos = pt;

                // 클릭아웃 감지 — suppress 무관, 항상 dispatch (v0.1.3)
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    try { _onLeftClickAnywhere(pt); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ClickAnywhere handler error: {ex}");
                        LogService.Log($"MouseHook: clickAnywhere handler error: {ex.Message}");
                    }
                }));
            }
            else if (msg == WM_LBUTTONUP && _downPoint.HasValue)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var up = new User32Interop.POINT { X = data.pt.x, Y = data.pt.y };
                var down = _downPoint.Value;
                _downPoint = null;

                var dx = Math.Abs(up.X - down.X);
                var dy = Math.Abs(up.Y - down.Y);
                var isDrag = (dx > DragThresholdPx || dy > DragThresholdPx);
                var isDoubleClick = _pendingDoubleClickUp;
                _pendingDoubleClickUp = false;

                if (DateTime.UtcNow >= _suppressUntil && (isDrag || isDoubleClick))
                {
                    var trigger = isDrag ? "drag" : "doubleclick";
                    LogService.Log($"MouseHook: trigger ({trigger}) dx={dx} dy={dy}");
                    _dispatcher.BeginInvoke(new Action(() =>
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
            Debug.WriteLine($"MouseHook callback error: {ex}");
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

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

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
