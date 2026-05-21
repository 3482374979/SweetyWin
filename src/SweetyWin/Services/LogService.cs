using System;
using System.IO;

namespace SweetyWin.Services;

/// <summary>
/// 진단용 파일 로거. `%LOCALAPPDATA%\SweetyWin\sweetywin.log` 에 append.
/// 사용자가 동작 안 함 신고 시 이 파일 공유받아 hook 설치 / 캡처 결과 확인.
/// </summary>
public static class LogService
{
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SweetyWin", "sweetywin.log");

    /// <summary>
    /// 정보성 로그(드래그·캡처·번역 매 호출 등) 활성화 여부.
    /// v0.1.4: 기본 OFF — 사용자가 진단 필요 시 설정에서 켬.
    /// 에러/실패는 항상 기록 (Log 메서드).
    /// </summary>
    public static bool EnableDiagnostic { get; set; }

    private static readonly object Sync = new();
    private const long MaxBytes = 1_000_000;
    // (v0.2.0) 매 쓰기마다 fs 체크는 비용 → 100건마다 체크
    private static int _writesSinceCheck;

    public static void Init()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(LogPath))
            {
                var info = new FileInfo(LogPath);
                if (info.Length > MaxBytes) File.Delete(LogPath);
            }
            // Init 메시지는 항상 기록 (간헐적 — 시작 시 1회)
            Log("=== SweetyWin started ===");
            Log($"Version: {typeof(LogService).Assembly.GetName().Version}");
            Log($"OS: {Environment.OSVersion}, 64bit: {Environment.Is64BitOperatingSystem}, Diagnostic: {EnableDiagnostic}");
        }
        catch { /* 로그 실패는 무시 */ }
    }

    /// <summary>항상 기록 — 에러·실패·시작/종료 등 드물고 중요한 이벤트.</summary>
    public static void Log(string msg)
    {
        try
        {
            lock (Sync)
            {
                // (v0.2.0) 매 100건 마다 회전 체크 — 장시간 실행 시 무한 성장 방지
                if (++_writesSinceCheck > 100)
                {
                    _writesSinceCheck = 0;
                    try
                    {
                        if (File.Exists(LogPath))
                        {
                            var info = new FileInfo(LogPath);
                            if (info.Length > MaxBytes) File.Delete(LogPath);
                        }
                    }
                    catch { /* 회전 실패 무시 */ }
                }
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
            }
        }
        catch { /* 디스크 풀/권한 등 무시 */ }
    }

    /// <summary>EnableDiagnostic=true 일 때만 기록 — 매 클릭/캡처/번역 등 빈번한 이벤트.</summary>
    public static void LogInfo(string msg)
    {
        if (!EnableDiagnostic) return;
        Log(msg);
    }
}
