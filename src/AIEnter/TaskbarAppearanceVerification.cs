using System.Text.Json;

namespace AIEnter;

public partial class App
{
    async Task VerifyTaskbarAppearance(bool abruptExit)
    {
        string resultPath = Path.Combine(DataDirectory, abruptExit ? "taskbar-crash-result.json" : "taskbar-result.json");
        try
        {
            string live = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIEnter"));
            if (!Environment.GetCommandLineArgs().Contains("--data-dir") || DataDirectory.Equals(live, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Taskbar checks require an isolated --data-dir.");
            Directory.CreateDirectory(DataDirectory);
            string native = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".tools", "windhawk", "AppData", "Engine", "ModsWritable", "aienter-taskbar-ai.ini"));
            async Task WaitFor(int glow, int size = UiDefaults.TaskbarIconSize)
            {
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    try
                    {
                        var fields = File.ReadAllLines(native).Where(line => line.Contains('=')).Select(line => line.Split('=', 2))
                            .GroupBy(pair => pair[0]).ToDictionary(group => group.Key, group => group.Last()[1]);
                        if (fields.GetValueOrDefault("GlowActive") == glow.ToString() && fields.GetValueOrDefault("ArtworkSize") == size.ToString()) return;
                    }
                    catch (IOException) { }
                    await Task.Delay(100);
                }
                throw new InvalidOperationException($"Native appearance did not reach glow={glow}, icon={size}.");
            }
            await WaitFor(0);
            await OpenAiWindow(); await WaitFor(1);
            if (abruptExit)
            {
                File.WriteAllText(resultPath, JsonSerializer.Serialize(new { passed = true, localPageOnly = true, abruptExitWithGlowActive = true }));
                Environment.Exit(0); // Test-only: exercise the native process-handle cleanup without normal shutdown.
            }
            Ai.Hide(); await WaitFor(0);
            OpenSettings(); await WaitFor(0);
            PreviewAppearance(new UiAppearance(TaskbarIconSize: 36, GlowRange: 12, GlowIntensity: 100));
            await WaitFor(0, 36);
            await OpenAiWindow(); await WaitFor(1, 36);
            settingsWindow!.Hide(); await WaitFor(1);
            PreviewAppearance(new UiAppearance(GlowEnabled: false)); await WaitFor(0);
            PreviewAppearance(new UiAppearance(GlowIntensity: 0)); await WaitFor(0);
            PreviewAppearance(new UiAppearance(GlowRange: 0)); await WaitFor(0);
            PreviewAppearance(null); await WaitFor(1);
            Ai.Hide(); await WaitFor(0);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { passed = true, localPageOnly = true,
                showLights = true, hideClears = true, settingsOnlyStaysOff = true, iconPreview = true,
                previewRollback = true, disabledAndZeroClear = true }));
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { passed = false, error = error.ToString() }));
        }
        finally { PreviewAppearance(null); QuitStandaloneAi(); }
    }
}
