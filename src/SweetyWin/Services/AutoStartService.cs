using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace SweetyWin.Services;

/// <summary>
/// Windows 로그인 시 자동 실행 — HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// HKCU 라 관리자 권한 불필요.
/// (v0.2.0) 자가 치유 — 매 시작 시 registry 의 stored path 와 현재 exe path 비교,
/// 다르면 갱신. 사용자가 exe 를 다른 폴더로 이동해도 다음 실행에서 자동 정정.
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
            LogService.Log($"AutoStart write failed: {ex.Message}");
        }
    }

    /// <summary>(v0.2.0) registry stored path 와 현재 exe path 비교, 다르면 갱신.
    /// App 시작 시 호출 — 자동 시작 활성 상태라면 항상 최신 경로 보장.</summary>
    public static void SyncRegistryToCurrentPath()
    {
        try
        {
            if (!IsEnabled) return;
            var stored = ReadStoredPath();
            var current = GetExecutablePath();
            if (string.IsNullOrEmpty(current)) return;
            var currentQuoted = $"\"{current}\"";
            if (!string.Equals(stored, currentQuoted, StringComparison.OrdinalIgnoreCase))
            {
                LogService.Log($"AutoStart: path drift detected — updating registry to {currentQuoted}");
                SetEnabled(true); // 재기록 — 현재 경로로 갱신
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"AutoStart sync failed: {ex.Message}");
        }
    }

    private static string? ReadStoredPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppRegistryName) as string;
        }
        catch { return null; }
    }

    private static string? GetExecutablePath()
    {
        var proc = Process.GetCurrentProcess();
        var path = proc.MainModule?.FileName;
        return Environment.ProcessPath ?? path;
    }
}
