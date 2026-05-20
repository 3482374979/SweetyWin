using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SweetyWin.Services;

namespace SweetyWin.Translation;

/// <summary>
/// 번역 오케스트레이터 — 사용 가능한 provider 중 우선순위 높은 것 선택.
/// 1) DeepL (API 키 설정 시) — 품질 우선
/// 2) MyMemory (기본 fallback) — 무료/무키
/// 사용자가 명시적 provider 를 강제하지 않는 한 자동 선택.
/// </summary>
public sealed class TranslationService
{
    private readonly List<ITranslationProvider> _providers;

    public TranslationService(SettingsService settings, HttpClient? sharedHttp = null)
    {
        var http = sharedHttp ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // 우선순위: 품질 좋은 것 먼저
        _providers = new List<ITranslationProvider>
        {
            new DeepLProvider(() => settings.Current.DeepLApiKey, http),
            new MyMemoryProvider(http),
        };
    }

    /// <summary>
    /// 텍스트 번역. source/target Auto 면 LanguageDetector + InferTarget 으로 추론.
    /// 첫 사용 가능한 provider 사용. 실패 시 다음 provider 로 fallback.
    /// </summary>
    public async Task<TranslationResult> TranslateAsync(
        string text,
        Language source = Language.Auto,
        Language target = Language.Auto,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult(string.Empty, source, target, "none");

        // 언어 추론
        var detected = source == Language.Auto ? LanguageDetector.Detect(text) : source;
        var resolvedTarget = target == Language.Auto
            ? LanguageDetector.InferTarget(detected)
            : target;

        Exception? lastError = null;
        foreach (var provider in _providers.Where(p => p.IsAvailable))
        {
            try
            {
                return await provider.TranslateAsync(text, detected, resolvedTarget, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                System.Diagnostics.Debug.WriteLine($"{provider.Name} failed: {ex.Message}");
                lastError = ex;
                // 다음 provider 로 fallback
            }
        }
        throw new InvalidOperationException("All translation providers failed", lastError);
    }
}
