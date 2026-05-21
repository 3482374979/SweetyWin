using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SweetyWin.Native;
using SweetyWin.Services;
using SweetyWin.Translation;
// FrameworkElement.Language(XmlLanguage) 가 우리 enum 을 가리는 문제 회피 — 별칭
using TLang = SweetyWin.Translation.Language;

namespace SweetyWin.Views;

/// <summary>
/// (v0.2.2) 번역 결과 viewer — 단순화.
/// 표시 즉시 번역 시작, 결과만 보여줌. 액션 메뉴(복사/사전/검색) 제거.
/// </summary>
public partial class QuickPopWindow : Window
{
    public string SelectedText { get; private set; } = string.Empty;

    private readonly SelectionService _selection;
    private readonly TranslationService _translation;
    private CancellationTokenSource? _translateCts;

    public QuickPopWindow(SelectionService selection, TranslationService translation)
    {
        InitializeComponent();
        _selection = selection;
        _translation = translation;
        SourceInitialized += OnSourceInitialized;
        Deactivated += OnDeactivated;
        KeyDown += OnKeyDown;
    }

    // WS_EX_TOOLWINDOW 만 — Alt+Tab/태스크바 비노출. NOACTIVATE 안 씀 (ESC/키 처리).
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = User32Interop.EnsureWindowHandle(this);
        var ex = User32Interop.GetWindowLongPtr(hwnd, User32Interop.GWL_EXSTYLE).ToInt64();
        ex |= User32Interop.WS_EX_TOOLWINDOW;
        User32Interop.SetWindowLongPtr(hwnd, User32Interop.GWL_EXSTYLE, new IntPtr(ex));
    }

    private void OnDeactivated(object? sender, EventArgs e) => HidePopup();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HidePopup();
            e.Handled = true;
        }
    }

    // ── 표시 진입점 ─────────────────────────────────────────────────
    public async Task ShowNearCursorAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            SelectedText = await _selection.CaptureAsync(cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Capture failed: {ex.Message}");
            SelectedText = string.Empty;
        }
        if (string.IsNullOrWhiteSpace(SelectedText)) return;

        ShowAtCursor();
        _ = StartTranslateAsync(SelectedText);
    }

    /// <summary>드래그/더블클릭 자동 트리거 — 미리 캡처된 텍스트 그대로.</summary>
    public void ShowWithText(string text)
    {
        SelectedText = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(SelectedText)) return;

        ShowAtCursor();
        _ = StartTranslateAsync(SelectedText);
    }

    public void ToggleNearCursor()
    {
        if (IsVisible) HidePopup();
        else _ = ShowNearCursorAsync();
    }

    private void ShowAtCursor()
    {
        if (!User32Interop.GetCursorPos(out var cursor)) return;

        Visibility = Visibility.Visible;
        UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(this);
        var widthPx = ActualWidth * dpi.DpiScaleX;
        var heightPx = ActualHeight * dpi.DpiScaleY;

        var targetX = cursor.X + 12;
        var targetY = cursor.Y + 18;

        var monitor = User32Interop.MonitorFromPoint(cursor, User32Interop.MONITOR_DEFAULTTONEAREST);
        var mi = new User32Interop.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<User32Interop.MONITORINFO>()
        };
        if (User32Interop.GetMonitorInfo(monitor, ref mi))
        {
            var work = mi.rcWork;
            if (targetX + widthPx > work.Right) targetX = (int)(work.Right - widthPx - 8);
            if (targetY + heightPx > work.Bottom) targetY = cursor.Y - (int)heightPx - 12;
            if (targetX < work.Left) targetX = work.Left + 8;
            if (targetY < work.Top) targetY = work.Top + 8;
        }

        Left = targetX / dpi.DpiScaleX;
        Top = targetY / dpi.DpiScaleY;
        Show();
    }

    private void HidePopup()
    {
        _translateCts?.Cancel();
        Hide();
    }

    // ── 자동 번역 ────────────────────────────────────────────────
    private async Task StartTranslateAsync(string text)
    {
        SourcePreview.Text = text.Replace("\r", " ").Replace("\n", " ");
        TranslatedText.Text = string.Empty;
        DirectionLabel.Text = string.Empty;
        LoadingBar.Visibility = Visibility.Visible;

        _translateCts?.Cancel();
        _translateCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = _translateCts.Token;

        try
        {
            var r = await _translation.TranslateAsync(text, TLang.Auto, TLang.Auto, token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            TranslatedText.Text = string.IsNullOrEmpty(r.Text) ? "(번역 결과 없음)" : r.Text;
            DirectionLabel.Text = $"{Label(r.DetectedSource)} → {Label(r.Target)} · {r.ProviderName}";
        }
        catch (OperationCanceledException)
        {
            // hide / 새 번역 시 cancel — 무동작
        }
        catch (Exception ex)
        {
            TranslatedText.Text = $"⚠ 번역 실패: {ex.Message}";
            DirectionLabel.Text = string.Empty;
            LogService.Log($"Translate: failed {ex.GetType().Name} {ex.Message}");
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    // ── 결과 복사 ───────────────────────────────────────────────────
    private void OnCopyResultClicked(object? sender, RoutedEventArgs e)
    {
        var t = TranslatedText.Text;
        if (!string.IsNullOrWhiteSpace(t))
        {
            try { Clipboard.SetText(t); } catch (Exception ex) { Debug.WriteLine(ex); }
        }
        HidePopup();
    }

    private static string Label(TLang lang) => lang switch
    {
        TLang.Korean => "한국어",
        TLang.English => "영어",
        TLang.Japanese => "일본어",
        TLang.Chinese => "중국어",
        TLang.Spanish => "스페인어",
        TLang.French => "프랑스어",
        TLang.German => "독일어",
        TLang.Russian => "러시아어",
        _ => "자동",
    };
}
