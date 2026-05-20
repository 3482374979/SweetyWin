using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using SweetyWin.Services;

namespace SweetyWin.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;

    public SettingsWindow(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadValues();
    }

    private void LoadValues()
    {
        var s = _settings.Current;
        DeepLKeyBox.Password = s.DeepLApiKey ?? string.Empty;
        AutoStartCheck.IsChecked = AutoStartService.IsEnabled;
        AutoShowOnDragCheck.IsChecked = s.AutoShowOnDragSelect;
        HotkeyDisplay.Text = FormatHotkey(s.HotkeyModifiers, s.HotkeyVk);
    }

    private static string FormatHotkey(uint mods, uint vk)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((mods & 0x02) != 0) parts.Add("Ctrl");
        if ((mods & 0x04) != 0) parts.Add("Shift");
        if ((mods & 0x01) != 0) parts.Add("Alt");
        if ((mods & 0x08) != 0) parts.Add("Win");
        parts.Add(VkToName(vk));
        return "현재: " + string.Join(" + ", parts);
    }

    private static string VkToName(uint vk) => vk switch
    {
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x1B => "Esc",
        >= 0x41 and <= 0x5A => ((char)vk).ToString(), // A-Z
        >= 0x30 and <= 0x39 => ((char)vk).ToString(), // 0-9
        >= 0x70 and <= 0x7B => $"F{vk - 0x70 + 1}",   // F1-F12
        _ => $"VK_{vk:X2}",
    };

    private void OnOpenSettingsFile(object? sender, RoutedEventArgs e)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SweetyWin", "settings.json");
        OpenFileSafe(path);
    }

    private void OnOpenLog(object? sender, RoutedEventArgs e)
    {
        OpenFileSafe(LogService.LogPath);
    }

    private void OpenFileSafe(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일 열기 실패: {ex.Message}\n경로: {path}",
                "SweetyWin", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var s = _settings.Current;

        // DeepL 키 — 빈 문자열이면 null 로 저장 (provider IsAvailable=false 로 떨어짐)
        var key = DeepLKeyBox.Password.Trim();
        s.DeepLApiKey = string.IsNullOrEmpty(key) ? null : key;

        // 드래그-자동 표시 — 변경은 다음 앱 시작부터 반영 (mouse hook 재설치 필요)
        s.AutoShowOnDragSelect = AutoShowOnDragCheck.IsChecked == true;

        _settings.Save();

        // 자동 시작 — 레지스트리 즉시 반영
        AutoStartService.SetEnabled(AutoStartCheck.IsChecked == true);

        DialogResult = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
