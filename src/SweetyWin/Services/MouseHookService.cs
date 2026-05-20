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

    private IntPtr _hook;
    private readonly LowLevelMouseProc _proc;  // GC 보호 — 콜백 델리게이트는 살아있어야
    private User32Interop.POINT? _downPoint;
    private readonly Action<User32Interop.POINT> _onDragComplete;
    private readonly Dispatcher _dispatcher;
    private DateTime _suppressUntil = DateTime.MinValue;

    public MouseHookService(Action<User32Interop.POINT> onDragComplete)
    {
        _onDragComplete = onDragComplete;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _proc = HookCallback;
        // 모듈 핸들은 0 (LL hook 은 모듈 불필요), dwThreadId=0 (모든 스레드)
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero)
        {
            Debug.WriteLine($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
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
                _downPoint = new User32Interop.POINT { X = data.pt.x, Y = data.pt.y };
            }
            else if (msg == WM_LBUTTONUP && _downPoint.HasValue)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var up = new User32Interop.POINT { X = data.pt.x, Y = data.pt.y };
                var down = _downPoint.Value;
                _downPoint = null;

                if (DateTime.UtcNow < _suppressUntil) return CallNextHookEx(_hook, nCode, wParam, lParam);

                var dx = Math.Abs(up.X - down.X);
                var dy = Math.Abs(up.Y - down.Y);
                if (dx > DragThresholdPx || dy > DragThresholdPx)
                {
                    // hook 콜백에서 블록 금지 — UI 스레드로 dispatch (BeginInvoke 비동기)
                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { _onDragComplete(up); }
                        catch (Exception ex) { Debug.WriteLine($"Drag handler error: {ex}"); }
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
