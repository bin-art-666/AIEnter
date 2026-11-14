namespace AIEnter;

// Approved defaults captured from saved UI preferences. No credentials, custom URLs or screen positions.
internal static class UiDefaults
{
    internal const int ButtonRadius = 16;
    internal const int ButtonSize = 30;
    internal const int WindowPadding = 4;
    internal const int TaskbarIconSize = 26;
    internal const bool GlowEnabled = true;
    internal const int GlowRange = 9;
    internal const int GlowIntensity = 77;
    internal const int BackplateRadius = 24;
    internal const string ButtonColor = "#1C14DDFF";
    internal const string SelectedButtonColor = "#FF32D4EB";
    internal const string Provider = "deepseek";
    internal const bool Pinned = false;
    internal const bool FloatingPanel = true;
    internal const double DockWidth = 401;
    internal const double DockHeight = 849;
    internal const bool FollowGlobalCorners = false;
    internal const int WebCornerRadius = 28;
    internal static string[] ProviderOrder => ["deepseek", "chatgpt", "doubao", "xiaoyi"];
}
