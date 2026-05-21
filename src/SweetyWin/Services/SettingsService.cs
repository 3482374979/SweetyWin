using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SweetyWin.Services;

public sealed class SweetyWinSettings
{
    public string? DeepLApiKey { get; set; }
    public string? PapagoClientId { get; set; }
    public string? PapagoClientSecret { get; set; }

    /// <summary>핫키 — VK 코드 + 수정자 비트마스크. 기본 Ctrl+Shift+Space.</summary>
    public uint HotkeyVk { get; set; } = 0x20;       // VK_SPACE
    public uint HotkeyModifiers { get; set; } = 0x06; // MOD_CONTROL(2) | MOD_SHIFT(4)

    /// <summary>드래그-선택 종료 시 자동으로 팝업 표시.
    /// (v0.2.1) 기본 OFF — 글로벌 마우스 후킹 환경별 변동성으로 안정성 우선.
    /// 옵트인 시만 hook 설치. 기존 사용자의 true 값은 그대로 유지(JSON 역직렬화).</summary>
    public bool AutoShowOnDragSelect { get; set; } = false;

    /// <summary>드래그-자동 표시 시 선택 텍스트 최소 길이 (단일 클릭 노이즈 필터).</summary>
    public int MinAutoShowTextLength { get; set; } = 2;

    /// <summary>(v0.1.4) 정보성 진단 로그 활성화 — 기본 OFF.</summary>
    public bool EnableDiagnosticLog { get; set; } = false;
}

/// <summary>
/// 설정 영속화 — JSON 파일을 `%LOCALAPPDATA%\SweetyWin\settings.json` 에 저장.
/// 첫 실행 시 기본값으로 파일 생성. API 키 등 민감 정보 포함 — 사용자 디렉토리 권한에 의존.
/// (DPAPI 암호화 강화는 Phase 4 이후 고려.)
/// </summary>
public sealed class SettingsService
{
    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SweetyWin");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // v0.1.4: 파일 IO 동시성 보호 — Load/Save 동시 호출 race 방지
    private readonly object _fileSync = new();

    public SweetyWinSettings Current { get; private set; } = new();

    public SettingsService()
    {
        Load();
    }

    public void Load()
    {
        lock (_fileSync)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    SaveUnlocked(); // 빈 기본 설정 생성
                    return;
                }
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<SweetyWinSettings>(json, JsonOpts);
                if (loaded != null) Current = loaded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings load failed: {ex.Message}");
            }
        }
    }

    public void Save()
    {
        lock (_fileSync) { SaveUnlocked(); }
    }

    private void SaveUnlocked()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(Current, JsonOpts);
            // (v0.2.0) 원자적 쓰기 — tmp 에 쓰고 NTFS atomic rename 으로 commit.
            // 도중 강제 종료 발생해도 settings.json 손상 없이 이전 상태 유지.
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings save failed: {ex.Message}");
        }
    }
}
