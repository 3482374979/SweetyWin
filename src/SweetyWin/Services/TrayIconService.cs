using System;
using System.Drawing;
using System.Windows.Forms;

namespace SweetyWin.Services;

/// <summary>
/// 시스템 트레이 아이콘 + 컨텍스트 메뉴.
/// 좌클릭 → QuickPop 토글. 우클릭 → 메뉴 (열기 / 설정 / 종료).
/// WPF 앱이지만 NotifyIcon 은 WinForms 라 UseWindowsForms=true 필요.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _disposed;

    public TrayIconService(Action onToggle, Action onSettings, Action onQuit)
    {
        _icon = new NotifyIcon
        {
            // TODO: 전용 .ico 추가. 임시로 시스템 아이콘 사용.
            Icon = SystemIcons.Application,
            Text = "SweetyWin — Ctrl+Shift+Space",
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
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
