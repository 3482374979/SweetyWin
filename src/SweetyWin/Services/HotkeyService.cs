using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using SweetyWin.Native;

namespace SweetyWin.Services;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0x0000,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    /// MOD_NOREPEAT — 키 누른 채로 반복 fire 안 함 (필수: 핫키 누름 1회당 1회 동작)
    NoRepeat = 0x4000,
}

/// <summary>
/// 글로벌 핫키 등록 — Win32 RegisterHotKey 기반.
/// 메시지 윈도우(HwndSource zero-size) 로 WM_HOTKEY 수신 후 등록된 action 실행.
/// macOS Sweety 의 CarbonHotkey (GlobalTriggerManager) 에 대응.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;
    private bool _disposed;

    public HotkeyService()
    {
        // 0x0 크기의 메시지 전용 윈도우 — HWND_MESSAGE 부모로 visible 없음
        var parameters = new HwndSourceParameters("SweetyWin.HotkeyMessageWindow")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>핫키 등록 — Application.OnStartup 등 UI 스레드에서 호출.</summary>
    /// <returns>등록 성공 시 hotkey id, 실패 시 -1.</returns>
    public int Register(HotkeyModifiers modifiers, VirtualKey key, Action action)
    {
        ObjectDisposedCheck();
        var id = _nextId++;
        var ok = User32Interop.RegisterHotKey(_source.Handle, id,
            (uint)(modifiers | HotkeyModifiers.NoRepeat), (uint)key);
        if (!ok)
        {
            return -1;
        }
        _handlers[id] = action;
        return id;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id))
        {
            User32Interop.UnregisterHotKey(_source.Handle, id);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32Interop.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var action))
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Hotkey handler error: {ex}");
                }
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ObjectDisposedCheck()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HotkeyService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _handlers.Keys)
        {
            User32Interop.UnregisterHotKey(_source.Handle, id);
        }
        _handlers.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
