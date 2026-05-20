using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SweetyWin.Native;

namespace SweetyWin.Views;

/// <summary>
/// QuickPop 팝업 윈도우 — nonactivating, topmost, 커서 근처 표시.
/// macOS Sweety 의 TextActionPaletteView + KeyablePanel(nonactivating) 에 대응.
/// </summary>
public partial class QuickPopWindow : Window
{
    /// 현재 표시 중인 선택 텍스트 (Phase 2 의 SelectionService 가 채워줌).
    public string SelectedText { get; set; } = string.Empty;

    public QuickPopWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Deactivated += OnDeactivated;
        KeyDown += OnKeyDown;
    }

    /// <summary>WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW 적용 — 클릭해도 포커스 안 뺏음.</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = User32Interop.EnsureWindowHandle(this);
        var ex = User32Interop.GetWindowLongPtr(hwnd, User32Interop.GWL_EXSTYLE).ToInt64();
        ex |= User32Interop.WS_EX_NOACTIVATE | User32Interop.WS_EX_TOOLWINDOW;
        User32Interop.SetWindowLongPtr(hwnd, User32Interop.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>포커스 잃으면 자동 숨김 (macOS resignKey 대응).</summary>
    private void OnDeactivated(object? sender, EventArgs e)
    {
        Hide();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    /// <summary>커서 근처에 표시 + 화면 경계 클램프.</summary>
    public void ShowNearCursor()
    {
        if (!User32Interop.GetCursorPos(out var cursor)) return;

        // SizeToContent 가 첫 측정 전이라면 Measure/Arrange 강제 (정확한 위치 계산용)
        if (!IsLoaded)
        {
            Visibility = Visibility.Visible;
            UpdateLayout();
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var widthPx = ActualWidth * dpi.DpiScaleX;
        var heightPx = ActualHeight * dpi.DpiScaleY;

        // 커서 약간 아래/오른쪽 (Sweety popupAnchor.belowRight 기본)
        var targetX = cursor.X + 12;
        var targetY = cursor.Y + 18;

        // 화면(작업 영역) 경계 클램프
        var monitor = User32Interop.MonitorFromPoint(cursor, User32Interop.MONITOR_DEFAULTTONEAREST);
        var mi = new User32Interop.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<User32Interop.MONITORINFO>() };
        if (User32Interop.GetMonitorInfo(monitor, ref mi))
        {
            var work = mi.rcWork;
            if (targetX + widthPx > work.Right) targetX = (int)(work.Right - widthPx - 8);
            if (targetY + heightPx > work.Bottom) targetY = cursor.Y - (int)heightPx - 12; // 위쪽으로
            if (targetX < work.Left) targetX = work.Left + 8;
            if (targetY < work.Top) targetY = work.Top + 8;
        }

        // WPF Left/Top 은 DIP(96 DPI) 단위. 픽셀 → DIP 변환.
        Left = targetX / dpi.DpiScaleX;
        Top = targetY / dpi.DpiScaleY;
        Show();
    }

    /// <summary>표시 중이면 숨김, 아니면 표시.</summary>
    public void ToggleNearCursor()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowNearCursor();
        }
    }

    // ── 액션 핸들러 (Phase 2/3 에서 실구현) ────────────────────────
    private void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            Clipboard.SetText(SelectedText);
        }
        Hide();
    }

    private void OnTranslateClicked(object? sender, RoutedEventArgs e)
    {
        // Phase 3: TranslationService 호출 → 결과 패널 확장
        Debug.WriteLine($"[Translate] selected: {SelectedText}");
        Hide();
    }

    private void OnDictionaryClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            var q = Uri.EscapeDataString(SelectedText);
            Process.Start(new ProcessStartInfo($"https://dict.naver.com/search.dict?query={q}") { UseShellExecute = true });
        }
        Hide();
    }

    private void OnSearchClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            var q = Uri.EscapeDataString(SelectedText);
            Process.Start(new ProcessStartInfo($"https://www.google.com/search?q={q}") { UseShellExecute = true });
        }
        Hide();
    }
}
