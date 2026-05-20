using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SweetyWin.Translation;

/// <summary>
/// DeepL Free API — 500k 자/월 무료. API 키(`xxx:fx`) 필요.
/// MyMemory 보다 번역 품질 월등. 키 설정 시 자동 우선 사용.
/// https://www.deepl.com/docs-api/translate-text
/// </summary>
public sealed class DeepLProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private readonly Func<string?> _apiKeyGetter;

    public DeepLProvider(Func<string?> apiKeyGetter, HttpClient? http = null)
    {
        _apiKeyGetter = apiKeyGetter;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SweetyWin/0.1");
    }

    public string Name => "DeepL";
    public bool RequiresApiKey => true;

    public bool IsAvailable
    {
        get
        {
            var key = _apiKeyGetter();
            return !string.IsNullOrWhiteSpace(key);
        }
    }

    public async Task<TranslationResult> TranslateAsync(
        string text, Language source, Language target, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult(string.Empty, source, target, Name);

        var key = _apiKeyGetter()
            ?? throw new InvalidOperationException("DeepL API key not set");

        // Free API 는 'xxx:fx' 키, endpoint 도 free 도메인
        var endpoint = key.EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate";

        var form = new List<KeyValuePair<string, string>>
        {
            new("text", text),
            new("target_lang", target.ToDeepLCode()),
        };
        if (source != Language.Auto)
        {
            form.Add(new("source_lang", source.ToDeepLCode()));
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", key);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<DeepLResponse>(stream, cancellationToken: ct).ConfigureAwait(false);

        var first = dto?.Translations?.Count > 0 ? dto.Translations[0] : null;
        var translated = first?.Text ?? string.Empty;
        var detected = ParseDeepLLang(first?.DetectedSourceLanguage);
        return new TranslationResult(translated, detected, target, Name);
    }

    private static Language ParseDeepLLang(string? code) => code?.ToUpperInvariant() switch
    {
        "EN" or "EN-GB" or "EN-US" => Language.English,
        "KO" => Language.Korean,
        "JA" => Language.Japanese,
        "ZH" => Language.Chinese,
        "ES" => Language.Spanish,
        "FR" => Language.French,
        "DE" => Language.German,
        "RU" => Language.Russian,
        _ => Language.Auto,
    };

    private sealed class DeepLResponse
    {
        [JsonPropertyName("translations")]
        public List<DeepLTranslation>? Translations { get; set; }
    }

    private sealed class DeepLTranslation
    {
        [JsonPropertyName("detected_source_language")]
        public string? DetectedSourceLanguage { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
