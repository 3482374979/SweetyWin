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
// FrameworkElement.Language(XmlLanguage) 가 우리 enum 을 가리는 문제 회피 — 별칭 사용
using TLang = SweetyWin.Translation.Language;

namespace SweetyWin.Views;

/// <summary>
/// QuickPop 팝업 윈도우 — nonactivating, topmost, 커서 근처 표시.
/// 액션: 복사 / 번역 / 사전 / 검색. 번역은 인라인 결과 패널로 확장.
/// </summary>
public partial class QuickPopWindow : Window
{
    /// 현재 선택된 텍스트 (SelectionService 가 채워줌).
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

    // ── Win32 스타일: WS_EX_TOOLWINDOW 만 — Alt+Tab/태스크바 비노출.
    // WS_EX_NOACTIVATE 는 안 씀 — ESC/키 입력 받으려면 활성화 필요.
    // (선택 텍스트는 ShowNearCursorAsync 첫줄에서 미리 캡처하므로 focus steal 무방.)
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = User32Interop.EnsureWindowHandle(this);
        var ex = User32Interop.GetWindowLongPtr(hwnd, User32Interop.GWL_EXSTYLE).ToInt64();
        ex |= User32Interop.WS_EX_TOOLWINDOW;
        User32Interop.SetWindowLongPtr(hwnd, User32Interop.GWL_EXSTYLE, new IntPtr(ex));
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        HidePopup();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // 번역 패널이 열려 있으면 패널만 닫고, 아니면 전체 hide
            if (TranslationPanel.Visibility == Visibility.Visible)
            {
                CloseTranslationPanel();
            }
            else
            {
                HidePopup();
            }
            e.Handled = true;
        }
    }

    // ── 표시/숨김 ─────────────────────────────────────────────────
    public async Task ShowNearCursorAsync()
    {
        LogService.LogInfo("ShowNearCursorAsync: hotkey trigger → capturing...");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            SelectedText = await _selection.CaptureAsync(cts.Token).ConfigureAwait(true);
            LogService.LogInfo($"ShowNearCursorAsync: captured {SelectedText.Length} chars");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Selection capture failed: {ex.Message}");
            LogService.LogInfo($"ShowNearCursorAsync: capture exception {ex.Message}");
            SelectedText = string.Empty;
        }

        CloseTranslationPanel();
        UpdateSelectionPreview();
        ShowAtCursor();
    }

    /// <summary>드래그-선택 자동 트리거 — 미리 캡처된 텍스트가 있으면 그대로 사용.</summary>
    public void ShowWithText(string text)
    {
        SelectedText = text ?? string.Empty;
        CloseTranslationPanel();
        UpdateSelectionPreview();
        ShowAtCursor();
    }

    private void UpdateSelectionPreview()
    {
        if (string.IsNullOrWhiteSpace(SelectedText))
        {
            SelectionPreview.Text = "선택 없음 — 텍스트를 드래그하거나 Ctrl+C 후 다시 시도";
            SelectionPreview.Opacity = 0.6;
        }
        else
        {
            var single = SelectedText.Replace("\r", " ").Replace("\n", " ");
            SelectionPreview.Text = $"“{single}”";
            SelectionPreview.Opacity = 1.0;
        }
    }

    public void ToggleNearCursor()
    {
        if (IsVisible)
        {
            HidePopup();
        }
        else
        {
            // fire-and-forget — UI 블록 안 함
            _ = ShowNearCursorAsync();
        }
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
        CloseTranslationPanel();
        Hide();
    }

    // ── 액션: 복사 ────────────────────────────────────────────────
    private void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            try { Clipboard.SetText(SelectedText); }
            catch (Exception ex) { Debug.WriteLine($"Clipboard set failed: {ex.Message}"); }
        }
        HidePopup();
    }

    // ── 액션: 번역 (인라인 패널 확장) ─────────────────────────────
    private async void OnTranslateClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedText))
        {
            // v0.1.1: 빈 선택 명시 피드백 — 사용자가 클릭이 안 먹는다고 오인 방지
            TranslationPanel.Visibility = Visibility.Visible;
            SourceTextBlock.Text = "(선택된 텍스트 없음)";
            TranslatedTextBlock.Text = "텍스트를 드래그-선택한 뒤 다시 시도하세요.\n또는 텍스트를 클립보드에 복사한 뒤 Ctrl+Shift+Space.";
            TranslationHeader.Text = "안내";
            ProviderLabel.Text = string.Empty;
            LoadingIndicator.Visibility = Visibility.Collapsed;
            UpdateLayout();
            return;
        }

        // 패널 표시 + 로딩 상태로
        SourceTextBlock.Text = SelectedText;
        TranslatedTextBlock.Text = string.Empty;
        ProviderLabel.Text = string.Empty;
        LoadingIndicator.Visibility = Visibility.Visible;
        TranslationPanel.Visibility = Visibility.Visible;
        UpdateLayout();

        // 이전 번역 취소
        _translateCts?.Cancel();
        _translateCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = _translateCts.Token;

        LogService.LogInfo($"Translate: requesting {SelectedText.Length} chars");
        try
        {
            var result = await _translation.TranslateAsync(SelectedText, TLang.Auto, TLang.Auto, token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            TranslatedTextBlock.Text = string.IsNullOrEmpty(result.Text) ? "(번역 결과 없음)" : result.Text;
            TranslationHeader.Text = $"{LanguageLabel(result.DetectedSource)} → {LanguageLabel(result.Target)}";
            ProviderLabel.Text = result.ProviderName;
            LogService.LogInfo($"Translate: success via {result.ProviderName}, {result.Text.Length} chars");
        }
        catch (OperationCanceledException)
        {
            LogService.LogInfo("Translate: cancelled");
        }
        catch (Exception ex)
        {
            TranslatedTextBlock.Text = $"⚠️ 번역 실패: {ex.Message}";
            ProviderLabel.Text = string.Empty;
            LogService.Log($"Translate: failed {ex.GetType().Name} {ex.Message}");
        }
        finally
        {
            LoadingIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCloseTranslationClicked(object? sender, RoutedEventArgs e)
    {
        CloseTranslationPanel();
    }

    private void CloseTranslationPanel()
    {
        _translateCts?.Cancel();
        TranslationPanel.Visibility = Visibility.Collapsed;
        SourceTextBlock.Text = string.Empty;
        TranslatedTextBlock.Text = string.Empty;
        LoadingIndicator.Visibility = Visibility.Collapsed;
    }

    private void OnCopyTranslationClicked(object? sender, RoutedEventArgs e)
    {
        var t = TranslatedTextBlock.Text;
        if (!string.IsNullOrWhiteSpace(t))
        {
            try { Clipboard.SetText(t); } catch (Exception ex) { Debug.WriteLine(ex); }
        }
        HidePopup();
    }

    // ── 액션: 사전 / 검색 (외부 브라우저) ─────────────────────────
    private void OnDictionaryClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            var q = Uri.EscapeDataString(SelectedText);
            OpenUrl($"https://dict.naver.com/search.dict?query={q}");
        }
        HidePopup();
    }

    private void OnSearchClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            var q = Uri.EscapeDataString(SelectedText);
            OpenUrl($"https://www.google.com/search?q={q}");
        }
        HidePopup();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenUrl failed: {ex.Message}");
        }
    }

    private static string LanguageLabel(TLang lang) => lang switch
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
