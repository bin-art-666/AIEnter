using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace AIEnter;

internal sealed partial class AiWindow
{
    (int Width, int Height, int Radius)? backplateShape;
    AiResizeFrame? resizeFrame;
    void InitializeBackplateCorners()
    {
        resizeFrame = new AiResizeFrame(WinRT.Interop.WindowNative.GetWindowHandle(this), () => Detached);
        Closed += (_, _) => resizeFrame.Dispose();
        PanelTheme.Changed += RefreshBackplateCorners;
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange || args.DidPositionChange) RefreshBackplateCorners();
        };
        root.Loaded += (_, _) => RefreshBackplateCorners();
        ((FrameworkElement)Content).SizeChanged += (_, _) => RefreshBackplateCorners();
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
        root.CornerRadius = backplate.CornerRadius = new CornerRadius(App.Appearance.BackplateRadius);
        // Grid.CornerRadius only rounds its own paint. Clip the complete content
        // visual so external glass and WebView composition layers cannot leak.
        var host = (Microsoft.UI.Xaml.Controls.Border)Content;
        host.CornerRadius = root.CornerRadius;
        if (host.ActualWidth > 0 && host.ActualHeight > 0)
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(host);
            var geometry = visual.Compositor.CreateRoundedRectangleGeometry();
            geometry.Size = new System.Numerics.Vector2((float)host.ActualWidth, (float)host.ActualHeight);
            geometry.CornerRadius = new System.Numerics.Vector2((float)App.Appearance.BackplateRadius);
            visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
        }
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
        if (SystemBackdrop is not GlassBackdrop { TransparentCanvas: true, IsClearCanvas: true })
            throw new Exception("Window canvas still paints a rectangular glass backdrop.");
        if (backplate.SystemBackdrop is not GlassBackdrop { IsReady: true } || backplate.CornerRadius.TopLeft != App.Appearance.BackplateRadius)
            throw new Exception("Glass material is not bound to the rounded backplate element.");
        if (Math.Abs(backplate.ActualWidth - ((FrameworkElement)Content).ActualWidth) > 1 || Math.Abs(backplate.ActualHeight - ((FrameworkElement)Content).ActualHeight) > 1)
            throw new Exception("Rounded glass does not fill the window client area.");
        var host = (Microsoft.UI.Xaml.Controls.Border)Content;
        var clip = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(host).Clip as Microsoft.UI.Composition.CompositionGeometricClip;
        var rounded = clip?.Geometry as Microsoft.UI.Composition.CompositionRoundedRectangleGeometry;
        if (rounded == null || rounded.CornerRadius.X != App.Appearance.BackplateRadius
            || Math.Abs(rounded.Size.X - host.ActualWidth) > 1 || Math.Abs(rounded.Size.Y - host.ActualHeight) > 1)
            throw new Exception("The full content composition layer is not clipped to the rounded silhouette.");
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var origin = new Native.Point(); ClientToScreen(handle, ref origin);
        GetBackplateWindowRect(handle, out var bounds); GetBackplateClientRect(handle, out var client);
        if (origin.X != bounds.Left || origin.Y != bounds.Top || client.Right != AppWindow.Size.Width || client.Bottom != AppWindow.Size.Height)
            throw new Exception("System non-client frame changed the visible backplate bounds.");
        var region = CreateRoundRectRgn(0, 0, 1, 1, 0, 0);
        try
        {
            if (GetWindowRgn(WinRT.Interop.WindowNative.GetWindowHandle(this), region) == 0)
                throw new Exception("Native backplate clip is missing.");
            var shape = backplateShape!.Value;
            int w = shape.Width, h = shape.Height, r = shape.Radius;
            if (w != AppWindow.Size.Width || h != AppWindow.Size.Height) throw new Exception("Backplate clip retained its previous size.");
            var expected = CreateRoundRectRgn(0, 0, w + 1, h + 1, r * 2, r * 2);
            try { if (!EqualRgn(region, expected)) throw new Exception("Native backplate no longer uses the configured circular corners."); }
            finally { DeleteObject(expected); }
            if (Detached) resizeFrame!.VerifyEdges();
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
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] static extern bool GetBackplateWindowRect(IntPtr window, out Native.Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetClientRect")] static extern bool GetBackplateClientRect(IntPtr window, out Native.Rect rect);
    [DllImport("gdi32.dll")] static extern bool EqualRgn(IntPtr first, IntPtr second);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
}
