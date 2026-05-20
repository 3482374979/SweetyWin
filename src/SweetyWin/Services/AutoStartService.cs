using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace SweetyWin.Services;

/// <summary>
/// Windows 로그인 시 자동 실행 — HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// HKCU 라 관리자 권한 불필요. HKLM 은 모든 사용자 적용용이지만 admin 필요해서 안 씀.
/// macOS Sweety 의 LaunchAgent plist 에 대응.
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "SweetyWin";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(AppRegistryName) != null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AutoStart read failed: {ex.Message}");
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key == null) return;

            if (enabled)
            {
                var exePath = GetExecutablePath();
                if (string.IsNullOrEmpty(exePath)) return;
                // 경로에 공백이 있어도 안전하도록 쌍따옴표로 감쌈
                key.SetValue(AppRegistryName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppRegistryName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AutoStart write failed: {ex.Message}");
        }
    }

    private static string? GetExecutablePath()
    {
        // PublishSingleFile 의 경우 MainModule.FileName 이 실제 exe 경로
        var proc = Process.GetCurrentProcess();
        var path = proc.MainModule?.FileName;
        // Environment.ProcessPath 가 더 신뢰성 있음 (.NET 6+)
        return Environment.ProcessPath ?? path;
    }
}
