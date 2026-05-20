using System;

namespace SweetyWin.Translation;

/// <summary>
/// 간이 언어 감지 — 유니코드 블록 기반 휴리스틱.
/// 한국어/일본어/중국어/영어 정도 구분. 정확한 감지는 API 의 auto-detect 에 위임.
/// </summary>
public static class LanguageDetector
{
    public static Language Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Language.Auto;

        int hangul = 0, hiragana = 0, katakana = 0, han = 0, latin = 0, cyrillic = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var c = rune.Value;
            if (c >= 0xAC00 && c <= 0xD7A3) hangul++;            // 한글 음절
            else if (c >= 0x3040 && c <= 0x309F) hiragana++;     // 히라가나
            else if (c >= 0x30A0 && c <= 0x30FF) katakana++;     // 가타카나
            else if (c >= 0x4E00 && c <= 0x9FFF) han++;          // CJK 통합 한자
            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            else if (c >= 0x0400 && c <= 0x04FF) cyrillic++;     // 키릴
        }

        // 우선순위: 한글 > 가나 > 한자 > 키릴 > 라틴
        if (hangul > 0) return Language.Korean;
        if (hiragana + katakana > 0) return Language.Japanese;
        if (han > 0) return Language.Chinese;
        if (cyrillic > 0) return Language.Russian;
        if (latin > 0) return Language.English;
        return Language.Auto;
    }

    /// <summary>소스 언어를 받아 사용자가 원할 만한 타겟 언어 추론.</summary>
    /// 한국어 → 영어, 그 외 → 한국어. (사용자가 한국어 사용자라는 가정)
    public static Language InferTarget(Language source) =>
        source == Language.Korean ? Language.English : Language.Korean;
}
