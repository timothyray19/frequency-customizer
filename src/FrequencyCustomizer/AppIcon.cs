using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace FrequencyCustomizer;

/// <summary>Draws the app icon (a sine wave in a circle) at runtime, so no binary assets are needed.</summary>
internal static class AppIcon
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(Color accent, bool active)
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(active ? accent : Color.FromArgb(70, 74, 86));
            g.FillEllipse(bg, 2, 2, 60, 60);
            var pts = new PointF[33];
            for (int i = 0; i < pts.Length; i++)
            {
                float x = 12 + i * 40f / (pts.Length - 1);
                pts[i] = new PointF(x, 32 - 13f * MathF.Sin((x - 12) / 40f * MathF.PI * 2));
            }
            using var pen = new Pen(Color.White, 6) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, pts);
        }
        IntPtr h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }
}
