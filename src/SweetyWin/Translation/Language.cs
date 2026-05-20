namespace SweetyWin.Translation;

/// <summary>
/// 번역 대상 언어 — ISO 639-1 코드. provider 별 매핑 책임.
/// 초기 지원: 한국어 ↔ 영어 (사용자 우선 케이스).
/// </summary>
public enum Language
{
    Auto,
    English,
    Korean,
    Japanese,
    Chinese,
    Spanish,
    French,
    German,
    Russian,
}

public static class LanguageExtensions
{
    /// <summary>ISO 639-1 코드.</summary>
    public static string ToIso(this Language lang) => lang switch
    {
        Language.Auto => "auto",
        Language.English => "en",
        Language.Korean => "ko",
        Language.Japanese => "ja",
        Language.Chinese => "zh",
        Language.Spanish => "es",
        Language.French => "fr",
        Language.German => "de",
        Language.Russian => "ru",
        _ => "auto",
    };

    /// <summary>DeepL 전용 코드 (대문자 + 일부 변형).</summary>
    public static string ToDeepLCode(this Language lang) => lang switch
    {
        Language.English => "EN",
        Language.Korean => "KO",
        Language.Japanese => "JA",
        Language.Chinese => "ZH",
        Language.Spanish => "ES",
        Language.French => "FR",
        Language.German => "DE",
        Language.Russian => "RU",
        _ => "EN", // DeepL 은 source=null 로 auto-detect, target 은 명시 필요
    };
}
