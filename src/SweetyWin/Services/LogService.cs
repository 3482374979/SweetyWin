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

    private static readonly object Sync = new();
    private const long MaxBytes = 1_000_000; // 1MB — 시작 시 초과면 회전

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
            Log("=== SweetyWin started ===");
            Log($"Version: {typeof(LogService).Assembly.GetName().Version}");
            Log($"OS: {Environment.OSVersion}, 64bit: {Environment.Is64BitOperatingSystem}");
        }
        catch
        {
            // 로그 자체가 실패하면 무시
        }
    }

    public static void Log(string msg)
    {
        try
        {
            lock (Sync)
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
            }
        }
        catch
        {
            // 로그 자체가 실패하면 무시 (디스크 풀 / 권한 등)
        }
    }
}
