using System;
using System.Windows;

namespace SweetyWin.Services;

/// <summary>
/// 클립보드 스냅샷 — 현재 텍스트/이미지를 저장하고 나중에 복원.
/// SelectionService 의 Ctrl+C fallback 이 클립보드를 오염시키므로 사용 전/후 호출.
/// </summary>
internal sealed class ClipboardSnapshot
{
    private readonly string? _text;
    private readonly System.Windows.Media.Imaging.BitmapSource? _image;

    private ClipboardSnapshot(string? text, System.Windows.Media.Imaging.BitmapSource? image)
    {
        _text = text;
        _image = image;
    }

    public static ClipboardSnapshot Capture()
    {
        string? text = null;
        System.Windows.Media.Imaging.BitmapSource? img = null;
        try
        {
            if (Clipboard.ContainsText())
            {
                text = Clipboard.GetText();
            }
            else if (Clipboard.ContainsImage())
            {
                img = Clipboard.GetImage();
            }
        }
        catch (Exception ex)
        {
            // 다른 프로세스가 클립보드를 열어둔 경우 등 — 무시하고 빈 스냅샷
            System.Diagnostics.Debug.WriteLine($"Clipboard capture failed: {ex.Message}");
        }
        return new ClipboardSnapshot(text, img);
    }

    public void Restore()
    {
        try
        {
            if (_text != null)
            {
                Clipboard.SetText(_text);
            }
            else if (_image != null)
            {
                Clipboard.SetImage(_image);
            }
            else
            {
                Clipboard.Clear();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard restore failed: {ex.Message}");
        }
    }
}
