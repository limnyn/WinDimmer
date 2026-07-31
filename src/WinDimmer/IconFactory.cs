using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Microsoft.Win32;
using WinDimmer.Native;
using static WinDimmer.Native.Constants;

namespace WinDimmer;

/// <summary>
/// 트레이 아이콘을 런타임에 그린다. 외부 .ico 에셋이 없어도 되고 DPI에 맞춰 선명하다.
/// </summary>
internal static class IconFactory
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>현재 DPI의 작은 아이콘 크기. 실패하면 16px로 떨어진다.</summary>
    public static int SmallIconSize()
    {
        int size = User32.GetSystemMetrics(SM_CXSMICON);
        return size > 0 ? size : 16;
    }

    /// <summary>
    /// 밝은 테마면 true. 값이 없거나 읽지 못하면 밝은 테마로 간주한다
    /// (어두운 아이콘이 밝은 작업표시줄에서 보이는 쪽이 안전한 기본값이다).
    /// </summary>
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is not int v || v != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    /// <summary>
    /// 창 모양 아이콘을 만든다. active면 오른쪽 절반을 채워 디밍 중임을 나타낸다.
    /// darkForeground면 어두운 색으로 그린다 (밝은 작업표시줄용).
    /// </summary>
    public static Icon Create(bool active, int size, bool darkForeground)
    {
        Color ink = darkForeground
            ? Color.FromArgb(230, 32, 32, 32)
            : Color.FromArgb(235, 245, 245, 245);

        // 큰 캔버스에 그린 뒤 축소하면 작은 크기에서 가장자리가 깨끗하다
        const int Canvas = 128;
        using var bmp = new Bitmap(Canvas, Canvas);

        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var body = new Rectangle(10, 14, 108, 100);
            const int Radius = 18;

            using var pen = new Pen(ink, 11f) { Alignment = PenAlignment.Center };
            using var brush = new SolidBrush(ink);
            using GraphicsPath path = RoundedRect(body, Radius);

            if (active)
            {
                // 오른쪽 절반 채우기 — 창 모양 밖으로 삐져나가지 않게 클리핑
                using var half = new Region(path);
                half.Intersect(new Rectangle(
                    body.Left + body.Width / 2, body.Top, body.Width / 2 + 1, body.Height));
                g.FillRegion(brush, half);
            }

            g.DrawPath(pen, path);

            // 제목 표시줄 선
            int titleY = body.Top + 26;
            g.DrawLine(pen, body.Left, titleY, body.Right, titleY);
        }

        using var scaled = new Bitmap(bmp, new Size(size, size));
        return ToIcon(scaled);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// GetHicon이 준 핸들은 GDI 리소스라 반드시 DestroyIcon으로 해제해야 한다.
    /// Icon.FromHandle은 소유권을 가져가지 않으므로 복제본만 남기고 원본을 즉시 파괴한다.
    /// </summary>
    private static Icon ToIcon(Bitmap bmp)
    {
        IntPtr handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            User32.DestroyIcon(handle);
        }
    }
}
