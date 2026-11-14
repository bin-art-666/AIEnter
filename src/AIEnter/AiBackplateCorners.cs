using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace AIEnter;

internal sealed partial class AiWindow
{
    (int Width, int Height, int Radius)? backplateShape;
    void InitializeBackplateCorners()
    {
        PanelTheme.Changed += RefreshBackplateCorners;
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange || args.DidPositionChange) RefreshBackplateCorners();
        };
        root.Loaded += (_, _) => RefreshBackplateCorners();
        Closed += (_, _) => PanelTheme.Changed -= RefreshBackplateCorners;
        RefreshBackplateCorners();
    }
    void RefreshBackplateCorners()
    {
        if (destroyed) return;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int width = AppWindow.Size.Width, height = AppWindow.Size.Height;
        if (width <= 0 || height <= 0) return;
        int radius = Math.Min(Math.Min(width, height) / 2,
            (int)Math.Round(App.Appearance.BackplateRadius * Native.GetDpiForWindow(handle) / 96d));
        root.CornerRadius = new CornerRadius(App.Appearance.BackplateRadius);
        if (backplateShape == (width, height, radius)) return;
        // Clip the native window as well as its XAML content: acrylic must not
        // remain visible behind the transparent corners. Windows owns a set HRGN.
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == IntPtr.Zero) throw new InvalidOperationException("无法创建背板圆角区域。");
        if (SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
            throw new InvalidOperationException("无法应用背板圆角区域。");
        }
        backplateShape = (width, height, radius);
    }
    internal void VerifyBackplateCorners()
    {
        RefreshBackplateCorners();
        var region = CreateRoundRectRgn(0, 0, 1, 1, 0, 0);
        try
        {
            if (GetWindowRgn(WinRT.Interop.WindowNative.GetWindowHandle(this), region) == 0)
                throw new Exception("Native backplate clip is missing.");
            var shape = backplateShape!.Value;
            int w = shape.Width, h = shape.Height, r = shape.Radius;
            if (PtInRegion(region, 0, 0) || PtInRegion(region, w - 1, 0)
                || PtInRegion(region, 0, h - 1) || PtInRegion(region, w - 1, h - 1)
                || !PtInRegion(region, r, 1) || !PtInRegion(region, w / 2, h / 2)
                || !PtInRegion(region, 1, h / 2))
                throw new Exception("Backplate corners are not transparent or the window interior was clipped.");
            if (root.CornerRadius.TopLeft != App.Appearance.BackplateRadius)
                throw new Exception("Backplate preview did not apply the current radius.");
        }
        finally { DeleteObject(region); }
    }
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
}
