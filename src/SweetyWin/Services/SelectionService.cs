using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using SweetyWin.Native;

namespace SweetyWin.Services;

/// <summary>
/// 시스템 전반의 선택된 텍스트를 캡처.
/// 1순위: UI Automation (UIA) — TextPattern.GetSelection. 클립보드 오염 없음.
/// 2순위: Ctrl+C → 클립보드 → 원래 클립보드 복원. UIA 미지원 앱(일부 Electron 등) fallback.
/// macOS Sweety 의 TextSelectionTrigger (NSAccessibility + Cmd+C fallback) 에 대응.
/// </summary>
public sealed class SelectionService
{
    // v0.1.1: 80ms 가 너무 짧아 일부 환경에서 클립보드 commit 못 잡음 → 250ms
    private const int ClipboardWaitMs = 250;
    private const int UiaTimeoutMs = 400;

    /// <summary>현재 포커스된 컨트롤의 선택 텍스트를 비동기로 캡처.</summary>
    /// <returns>선택 텍스트. 캡처 실패 시 빈 문자열.</returns>
    public async Task<string> CaptureAsync(CancellationToken ct = default)
    {
        // 1) UIA 시도 — 빠르고 비파괴
        var uia = await TryUiaAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(uia)) return uia;

        // 2) 클립보드 fallback — Ctrl+C 시뮬레이션
        return await TryClipboardFallbackAsync(ct).ConfigureAwait(false);
    }

    // ── UIA 경로 ──────────────────────────────────────────────────
    private static Task<string> TryUiaAsync(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                // UIA 호출이 hang 할 가능성 — 타임아웃 보호
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(UiaTimeoutMs);

                var focused = AutomationElement.FocusedElement;
                if (focused == null) return string.Empty;

                if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObj)
                    && patternObj is TextPattern textPattern)
                {
                    var ranges = textPattern.GetSelection();
                    if (ranges != null && ranges.Length > 0)
                    {
                        var text = ranges[0].GetText(-1);
                        return text?.Trim() ?? string.Empty;
                    }
                }
                // TextPattern 미지원 — UIA 로는 못 가져옴
                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UIA selection failed: {ex.Message}");
                return string.Empty;
            }
        }, ct);
    }

    // ── 클립보드 fallback ────────────────────────────────────────
    private static async Task<string> TryClipboardFallbackAsync(CancellationToken ct)
    {
        // 호출 스레드(UI 스레드)에서 클립보드 작업 — STA 필요
        // Application.Current?.Dispatcher 로 보장
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return string.Empty;

        return await dispatcher.InvokeAsync(async () =>
        {
            var snapshot = ClipboardSnapshot.Capture();
            try
            {
                // 클립보드 비우기 → 다음 텍스트 도착 감지 명확화
                try { Clipboard.Clear(); } catch { /* ignore */ }

                // Ctrl+C 전송 — 현재 포커스 컨트롤에 적용
                User32Interop.SendCtrlC();

                // 텍스트 도착 폴링 (총 ClipboardWaitMs 한도)
                var deadline = DateTime.UtcNow.AddMilliseconds(ClipboardWaitMs);
                while (DateTime.UtcNow < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Clipboard.ContainsText())
                    {
                        var t = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(t)) return t.Trim();
                    }
                    await Task.Delay(10, ct).ConfigureAwait(true);
                }
                return string.Empty;
            }
            finally
            {
                // 원래 클립보드 복원 — 사용자 데이터 유지
                snapshot.Restore();
            }
        }).Task.Unwrap().ConfigureAwait(false);
    }
}
