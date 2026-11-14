using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Security.Principal;

namespace AIEnter;

public partial class App
{
    EventWaitHandle? aiOpenEvent, aiQuitEvent, settingsEvent;
    RegisteredWaitHandle? aiOpenWait, aiQuitWait, settingsWait;
    Window? aiLifetimeWindow;
    SettingsWindow? settingsWindow;
    bool aiOpening, aiQuitting;

    void LaunchAiOnly(string[] arguments)
    {
        using var identity = WindowsIdentity.GetCurrent();
        string prefix = "Local\\AIEnter." + identity.User!.Value;
        instance = new Mutex(true, prefix, out bool first);
        aiOpenEvent = new(false, EventResetMode.AutoReset, prefix + ".Open");
        aiQuitEvent = new(false, EventResetMode.AutoReset, prefix + ".Quit");
        settingsEvent = new(false, EventResetMode.AutoReset, prefix + ".Settings");
        InitializeToggleEntry(prefix);
        if (!first || arguments.Contains("--quit"))
        {
            if (!first)
            {
                if (arguments.Contains("--toggle-ai")) SignalToggle(arguments);
                else if (arguments.Contains("--taskbar-drop") || arguments.Contains("--drag-hover"))
                    SignalDropEntry(prefix, arguments.Contains("--taskbar-drop"));
                else (arguments.Contains("--quit") ? aiQuitEvent : arguments.Contains("--settings") ? settingsEvent : aiOpenEvent).Set();
            }
            DisposeToggleEntry(); aiOpenEvent.Dispose(); aiQuitEvent.Dispose(); settingsEvent.Dispose(); instance.Dispose();
            Exit(); return;
        }
        PanelTheme.Apply(UiStyle.Data);
        if (!arguments.Any(a => a.StartsWith("--verify-") || a is "--taskbar-drop" or "--drag-hover" or "--toggle-ai") && !Ai.Options.ReadOnly)
        {
            var saved = Ai.Options.Data;
            string preferred = saved.DefaultProvider ?? UiDefaults.Provider;
            if (saved.Provider != preferred) Ai.Options.Save(saved with { Provider = preferred });
        }
        StartAppearanceBridge();
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        // A hidden window keeps the WinUI lifetime independent of the toolbar.
        aiLifetimeWindow = new Window { Title = "AIEnter 后台" };
        _ = WinRT.Interop.WindowNative.GetWindowHandle(aiLifetimeWindow);
        aiOpenWait = ThreadPool.RegisterWaitForSingleObject(aiOpenEvent,
            (_, _) => dispatcher.TryEnqueue(() => _ = OpenAiWindow()), null, Timeout.Infinite, false);
        aiQuitWait = ThreadPool.RegisterWaitForSingleObject(aiQuitEvent,
            (_, _) => dispatcher.TryEnqueue(QuitStandaloneAi), null, Timeout.Infinite, true);
        settingsWait = ThreadPool.RegisterWaitForSingleObject(settingsEvent,
            (state, timedOut) => dispatcher.TryEnqueue(OpenSettings), null, Timeout.Infinite, false);
        InitializeDropEntry(prefix, dispatcher);
        toggleWait = ThreadPool.RegisterWaitForSingleObject(toggleEvent!, (_, _) => dispatcher.TryEnqueue(() => _ = ToggleAiWindow()), null, Timeout.Infinite, false);
        if (arguments.Contains("--taskbar-drop") || Directory.Exists(Path.Combine(DataDirectory, "incoming-drops")) && Directory.EnumerateFiles(Path.Combine(DataDirectory, "incoming-drops"), "*.json").Any()) _ = DrainTaskbarDrops();
        else if (arguments.Contains("--drag-hover")) _ = OpenDropEntry(false);
        else if (arguments.Contains("--verify-toggle")) _ = VerifyToggleEntry();
        else if (arguments.Any(a => a is "--verify-taskbar" or "--verify-taskbar-crash"))
            _ = VerifyTaskbarAppearance(arguments.Contains("--verify-taskbar-crash"));
        else if (arguments.Contains("--toggle-ai")) { SignalToggle(arguments); }
        else if (arguments.Contains("--settings")) OpenSettings();
        else _ = OpenAiWindow();
    }

    void OpenSettings()
    {
        if (aiQuitting) return;
        settingsWindow ??= new SettingsWindow(Ai.Options, QuitStandaloneAi);
        settingsWindow.Open();
    }

    async Task OpenAiWindow()
    {
        if (aiOpening || aiQuitting) return;
        aiOpening = true;
        try
        {
            var monitors = Native.Monitors();
            await Ai.OpenPreferred(monitors.First(m => m.Primary));
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(Path.Combine(DataDirectory, "ai-launch.log"), $"{DateTimeOffset.Now:O} {error}\n");
        }
        finally { aiOpening = false; }
    }

    void QuitStandaloneAi()
    {
        if (aiQuitting) return;
        aiQuitting = true;
        aiOpenWait?.Unregister(null); aiQuitWait?.Unregister(null); settingsWait?.Unregister(null);
        DisposeToggleEntry(); DisposeDropEntry(); settingsWindow?.Destroy(); StopAppearanceBridge(); StopAi(); aiLifetimeWindow?.Close();
        aiOpenEvent?.Dispose(); aiQuitEvent?.Dispose(); settingsEvent?.Dispose(); instance?.Dispose();
        Exit();
    }
}
