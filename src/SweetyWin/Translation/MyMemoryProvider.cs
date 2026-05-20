using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SweetyWin.Translation;

/// <summary>
/// MyMemory 번역 API — 무료, API 키 불필요, ~1000 단어/일.
/// 기본 fallback provider. 품질은 DeepL/Papago 보다 낮으나 키 없이 즉시 동작.
/// https://mymemory.translated.net/doc/spec.php
/// </summary>
public sealed class MyMemoryProvider : ITranslationProvider
{
    private readonly HttpClient _http;

    public MyMemoryProvider(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SweetyWin/0.1");
    }

    public string Name => "MyMemory";
    public bool RequiresApiKey => false;
    public bool IsAvailable => true;

    public async Task<TranslationResult> TranslateAsync(
        string text, Language source, Language target, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult(string.Empty, source, target, Name);

        // langpair=ko|en 같은 ISO 코드 페어. source=auto 인 경우 감지 후 페어 구성.
        var srcCode = source == Language.Auto ? LanguageDetector.Detect(text).ToIso() : source.ToIso();
        if (srcCode == "auto") srcCode = "en"; // 폴백
        var tgtCode = target.ToIso();

        var url = "https://api.mymemory.translated.net/get?q="
                  + HttpUtility.UrlEncode(text)
                  + $"&langpair={srcCode}|{tgtCode}";

        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<MyMemoryResponse>(stream, cancellationToken: ct).ConfigureAwait(false);

        var translated = dto?.ResponseData?.TranslatedText ?? string.Empty;
        var detectedSrc = source == Language.Auto ? LanguageDetector.Detect(text) : source;
        return new TranslationResult(translated, detectedSrc, target, Name);
    }

    private sealed class MyMemoryResponse
    {
        [JsonPropertyName("responseData")]
        public MyMemoryResponseData? ResponseData { get; set; }
    }

    private sealed class MyMemoryResponseData
    {
        [JsonPropertyName("translatedText")]
        public string? TranslatedText { get; set; }
    }
}
