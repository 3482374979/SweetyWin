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

    /// <summary>드래그-선택 종료 시 자동으로 팝업 표시.</summary>
    public bool AutoShowOnDragSelect { get; set; } = true;

    /// <summary>드래그-자동 표시 시 선택 텍스트 최소 길이 (단일 클릭 노이즈 필터).</summary>
    public int MinAutoShowTextLength { get; set; } = 2;
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

    public SweetyWinSettings Current { get; private set; } = new();

    public SettingsService()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Save(); // 빈 기본 설정 생성
                return;
            }
            var json = File.ReadAllText(SettingsPath);
            var loaded = JsonSerializer.Deserialize<SweetyWinSettings>(json, JsonOpts);
            if (loaded != null) Current = loaded;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings load failed: {ex.Message}");
            // 손상된 설정 — 기본값으로 진행
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(Current, JsonOpts);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings save failed: {ex.Message}");
        }
    }
}
