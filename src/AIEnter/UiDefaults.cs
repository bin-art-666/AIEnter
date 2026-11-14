namespace AIEnter;

// Approved defaults captured from saved UI preferences. Only public AI entry URLs; no credentials, browser data or screen positions.
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
    internal const double DockWidth = 415.5;
    internal const double DockHeight = 829;
    internal const bool FollowGlobalCorners = false;
    internal const int WebCornerRadius = 28;
    internal static string[] ProviderOrder => ["deepseek", "chatgpt", "doubao", "xiaoyi", "ai-gemini", "ai-grok", "ai-kimi"];
    internal static AiProvider[] AddedProviders =>
    [
        new("ai-gemini", "Gemini", "https://gemini.google.com/app?utm_source=app_launcher&utm_medium=owned&utm_campaign=base_all"),
        new("ai-grok", "Grok", "https://grok.com/"),
        new("ai-kimi", "Kimi", "https://www.kimi.com/en")
    ];
    internal static AiOptions CreateOptions() => new(Layouts: [], ProviderOrder: ProviderOrder,
        AddedProviders: AddedProviders, DefaultProvider: Provider);
}
