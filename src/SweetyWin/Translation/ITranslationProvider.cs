using System.Threading;
using System.Threading.Tasks;

namespace SweetyWin.Translation;

public readonly record struct TranslationResult(
    string Text,
    Language DetectedSource,
    Language Target,
    string ProviderName
);

public interface ITranslationProvider
{
    /// <summary>표시 이름 — UI 표기/로그용.</summary>
    string Name { get; }

    /// <summary>API 키 없이 동작 가능?</summary>
    bool RequiresApiKey { get; }

    /// <summary>현재 설정으로 사용 가능?</summary>
    bool IsAvailable { get; }

    Task<TranslationResult> TranslateAsync(
        string text,
        Language source,
        Language target,
        CancellationToken ct = default
    );
}
