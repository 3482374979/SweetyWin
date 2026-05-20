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
/// 1순위: UI Automation (UIA) — TextPattern.GetSelection. 클립보드 무오염.
/// 2순위: Ctrl+C → 클립보드 시퀀스 번호 감시 → 결과 읽기 → 원래 클립보드 복원.
///   v0.1.2: Clipboard.ContainsText 폴링 대신 GetClipboardSequenceNumber 변화 감지
///   (race-free, 가볍고 다른 앱 클립보드 락 간섭 회피).
/// </summary>
public sealed class SelectionService
{
    private static readonly TimeSpan UiaTimeout = TimeSpan.FromMilliseconds(600);
    // v0.1.3: 600 → 1000ms. Office/Acrobat 등 무거운 앱 응답 대기.
    private static readonly TimeSpan ClipboardTimeout = TimeSpan.FromMilliseconds(1000);

    public async Task<string> CaptureAsync(CancellationToken ct = default)
    {
        // 1) UIA — 깨끗하고 빠름
        var uia = await TryUiaAsync(UiaTimeout, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(uia))
        {
            LogService.Log($"Capture: UIA succeeded ({uia.Length} chars)");
            return uia;
        }
        LogService.Log("Capture: UIA empty/failed → Ctrl+C fallback");

        // 2) Ctrl+C fallback
        var clip = await TryClipboardFallbackAsync(ClipboardTimeout, ct).ConfigureAwait(false);
        LogService.Log($"Capture: Ctrl+C result ({clip.Length} chars)");
        return clip;
    }

    // ── UIA 경로 ──────────────────────────────────────────────────
    private static async Task<string> TryUiaAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            // COM 호출이 hang 가능 — Task.Run + 타임아웃 race
            var work = Task.Run(() =>
            {
                try
                {
                    var focused = AutomationElement.FocusedElement;
                    if (focused == null)
                    {
                        LogService.Log("UIA: FocusedElement=null");
                        return string.Empty;
                    }

                    // (v0.1.3) 1. focused 자신 → ancestor 5단계 까지 TextPattern 탐색
                    var t1 = TryTextPatternAncestor(focused);
                    if (!string.IsNullOrEmpty(t1)) return t1;

                    // (v0.1.3) 2. LegacyIAccessiblePattern (MSAA) fallback — 일부 win32 legacy 앱
                    var t2 = TryLegacyPattern(focused);
                    if (!string.IsNullOrEmpty(t2)) return t2;

                    LogService.Log($"UIA: no TextPattern/Legacy in {focused.Current.ControlType?.ProgrammaticName}");
                    return string.Empty;
                }
                catch (Exception ex)
                {
                    LogService.Log($"UIA exception: {ex.GetType().Name} {ex.Message}");
                    return string.Empty;
                }
            }, cts.Token);

            var timeoutTask = Task.Delay(timeout, ct);
            var done = await Task.WhenAny(work, timeoutTask).ConfigureAwait(false);
            if (done == timeoutTask)
            {
                LogService.Log("UIA: timed out");
                return string.Empty;
            }
            return await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }

    /// <summary>(v0.1.3) focused element 자신부터 위로 5단계 — TextPattern 보유 ancestor 탐색.</summary>
    private static string TryTextPatternAncestor(AutomationElement element)
    {
        var current = element;
        for (int i = 0; i < 5 && current != null; i++)
        {
            try
            {
                if (current.TryGetCurrentPattern(TextPattern.Pattern, out var p) && p is TextPattern tp)
                {
                    var ranges = tp.GetSelection();
                    if (ranges != null && ranges.Length > 0)
                    {
                        var text = ranges[0].GetText(-1)?.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            LogService.Log($"UIA: TextPattern hit at depth {i}");
                            return text;
                        }
                    }
                }
            }
            catch { /* 일부 ancestor 가 throw — 무시하고 위로 */ }

            try { current = TreeWalker.RawViewWalker.GetParent(current); }
            catch { break; }
        }
        return string.Empty;
    }

    /// <summary>(v0.1.3) LegacyIAccessiblePattern — MSAA 기반, UIA TextPattern 미지원 앱용 fallback.</summary>
    private static string TryLegacyPattern(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(LegacyIAccessiblePattern.Pattern, out var lp)
                && lp is LegacyIAccessiblePattern legacy)
            {
                // GetSelection() 으로 선택 항목 → Name 또는 Value
                try
                {
                    var sel = legacy.GetSelection();
                    if (sel != null && sel.Length > 0)
                    {
                        var n = sel[0].Current.Name;
                        if (!string.IsNullOrEmpty(n))
                        {
                            LogService.Log("UIA: Legacy GetSelection Name hit");
                            return n.Trim();
                        }
                    }
                }
                catch { /* GetSelection 미지원 — Value 시도 */ }

                var v = legacy.Current.Value;
                if (!string.IsNullOrEmpty(v))
                {
                    LogService.Log("UIA: Legacy Value hit");
                    return v.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"UIA Legacy exception: {ex.Message}");
        }
        return string.Empty;
    }

    // ── 클립보드 fallback ────────────────────────────────────────
    private static async Task<string> TryClipboardFallbackAsync(TimeSpan timeout, CancellationToken ct)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            LogService.Log("Ctrl+C: no dispatcher");
            return string.Empty;
        }

        return await dispatcher.InvokeAsync(async () =>
        {
            var snapshot = ClipboardSnapshot.Capture();
            try
            {
                // 시퀀스 번호로 변화 감지 (Clipboard.ContainsText 폴링보다 안정적)
                var initialSeq = User32Interop.GetClipboardSequenceNumber();
                User32Interop.SendCtrlC();

                var deadline = DateTime.UtcNow + timeout;
                while (DateTime.UtcNow < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    var currentSeq = User32Interop.GetClipboardSequenceNumber();
                    if (currentSeq != initialSeq)
                    {
                        // 클립보드 변화 감지 → 텍스트 읽기 (재시도 3회 — busy 시 대응)
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            try
                            {
                                if (Clipboard.ContainsText())
                                {
                                    var t = Clipboard.GetText();
                                    return string.IsNullOrEmpty(t) ? string.Empty : t.Trim();
                                }
                                LogService.Log("Ctrl+C: clipboard changed but ContainsText=false");
                                return string.Empty;
                            }
                            catch (Exception ex)
                            {
                                LogService.Log($"Ctrl+C: read attempt {attempt + 1} failed: {ex.Message}");
                                await Task.Delay(20, ct).ConfigureAwait(true);
                            }
                        }
                        return string.Empty;
                    }
                    await Task.Delay(8, ct).ConfigureAwait(true);
                }
                LogService.Log("Ctrl+C: timed out — clipboard sequence didn't change");
                return string.Empty;
            }
            catch (OperationCanceledException)
            {
                return string.Empty;
            }
            finally
            {
                snapshot.Restore();
            }
        }).Task.Unwrap().ConfigureAwait(false);
    }
}
