using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Interop;

namespace SweetyWin.Services;

/// <summary>
/// 시스템 트레이 아이콘 + 컨텍스트 메뉴.
/// (v0.2.0) explorer.exe 재시작 시 트레이 부활 — RegisterWindowMessage("TaskbarCreated")
/// 메시지 수신 후 NotifyIcon Visible 토글로 자동 복귀. UpdateTooltip() 으로 상태 반영.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly HwndSource _msgWindow;
    private readonly uint _taskbarCreatedMsg;
    private bool _disposed;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    public TrayIconService(Action onToggle, Action onSettings, Action onQuit)
    {
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "SweetyWin",
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => onToggle());
        menu.Items.Add("설정...", null, (_, _) => onSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => onQuit());
        _icon.ContextMenuStrip = menu;

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) onToggle();
        };

        // (v0.2.0) explorer.exe 재시작 대응 — TaskbarCreated 메시지 수신용 message window
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        var parameters = new HwndSourceParameters("SweetyWin.TrayMessageWindow")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };
        _msgWindow = new HwndSource(parameters);
        _msgWindow.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreatedMsg)
        {
            try
            {
                LogService.Log("Tray: TaskbarCreated received — re-creating icon");
                _icon.Visible = false;
                _icon.Visible = true;
            }
            catch (Exception ex)
            {
                LogService.Log($"Tray: TaskbarCreated handler failed: {ex.Message}");
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>(v0.2.0) 상태에 따라 트레이 툴팁 업데이트 — 사용자에게 시각 피드백.</summary>
    public void UpdateTooltip(string status)
    {
        try { _icon.Text = status.Length > 63 ? status[..63] : status; } // NotifyIcon.Text 63자 제한
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _icon.Visible = false; _icon.Dispose(); } catch { }
        try { _msgWindow.RemoveHook(WndProc); _msgWindow.Dispose(); } catch { }
    }
}
