using Microsoft.UI.Xaml;

namespace AIEnter;

public partial class App : Application
{
    Mutex? instance;
    internal static string DataDirectory { get; private set; } = "";
    static AiService? ai;
    static UiStyleStore? uiStyle;
    internal static AiService Ai => ai ??= new AiService(DataDirectory, verification: Environment.GetCommandLineArgs().Any(a => a is "--verify-taskbar" or "--verify-taskbar-crash"));
    static UiStyleStore UiStyle => uiStyle ??= new(Path.Combine(DataDirectory, "ui-style.json"));
    internal static void StopAi() { ai?.Stop(); ai = null; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.AppendAllText(Path.Combine(DataDirectory, "error.log"), $"{DateTimeOffset.Now:O} {args.Exception}\n");
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(arguments, "--data-dir");
        DataDirectory = index >= 0 && index + 1 < arguments.Length
            ? Path.GetFullPath(arguments[index + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIEnter");
        if (arguments.Contains("--verify-ai-modes"))
        {
            PanelTheme.Apply(UiStyle.Data);
            _ = VerifyAiModes();
            return;
        }
        LaunchAiOnly(arguments);
    }
}
