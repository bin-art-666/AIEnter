using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace AIEnter;

public partial class App
{
    async Task VerifyAiSiteCapabilities()
    {
        string reportPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".build", "ai-site-capability-report.json"));
        var rows = new List<object>(); Window? window = null; WebView2? view = null;
        try
        {
            window = new Window { Title = "AIEnter · 网站接收能力检查" };
            view = new WebView2(); window.Content = view;
            _ = WinRT.Interop.WindowNative.GetWindowHandle(window);
            window.AppWindow.MoveAndResize(new(-30000, -30000, 980, 720));
            window.AppWindow.Show(false);
            AiOptionsStore store = Ai.Options;
            foreach (string id in AiPolicy.OrderedProviders(store.Data))
            {
                AiEndpoint endpoint;
                try { endpoint = AiPolicy.Resolve(store.Data with { Provider = id }); }
                catch { continue; }
                object row;
                try
                {
                    string profile = Path.Combine(DataDirectory, "AiWebProfiles", endpoint.Key);
                    var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profile, null);
                    await view.EnsureCoreWebView2Async(environment);
                    var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Navigated(object? sender, CoreWebView2NavigationCompletedEventArgs args) => loaded.TrySetResult(args.IsSuccess);
                    view.CoreWebView2.NavigationCompleted += Navigated;
                    view.CoreWebView2.Navigate(endpoint.Url);
                    bool loadSucceeded = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    await Task.Delay(800); // Allow single-page apps to render their composer after initial navigation.
                    string host = Uri.TryCreate(view.CoreWebView2.Source, UriKind.Absolute, out var uri) ? uri.Host : "";
                    string script = "(()=>{const v=e=>!!e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden';const ed=[...document.querySelectorAll('textarea:not([disabled]):not([readonly]),[contenteditable=true][role=textbox],[contenteditable=true]')].filter(v);const fi=[...document.querySelectorAll('input[type=file]:not([disabled])')];return {ready:document.readyState,editors:ed.length,fileInputs:fi.length,loginFields:[...document.querySelectorAll('input[type=password]')].filter(v).length};})()";
                    var capabilities = JsonDocument.Parse(await view.CoreWebView2.ExecuteScriptAsync(script));
                    int editorCount = capabilities.RootElement.GetProperty("editors").GetInt32();
                    int inputCount = capabilities.RootElement.GetProperty("fileInputs").GetInt32();
                    bool image = editorCount > 0 && await DetectFileInput(view, ["fixture.png"]);
                    bool document = editorCount > 0 && await DetectFileInput(view, ["fixture.pdf"]);
                    bool multiple = editorCount > 0 && await DetectFileInput(view, ["fixture.png", "fixture.pdf"]);
                    row = new { service = endpoint.Name, host, loaded = loadSucceeded, editors = editorCount, fileInputs = inputCount,
                        imageInput = image, documentInput = document, multipleFilesInput = multiple, result = editorCount > 0 && image && document && multiple ? "composer-and-input-ready" : "login-or-site-state-needs-review" };
                    view.CoreWebView2.NavigationCompleted -= Navigated;
                    view.Close(); view = new WebView2(); window.Content = view;
                }
                catch (Exception error)
                {
                    row = new { service = endpoint.Name, loaded = false, result = error is TimeoutException ? "navigation-timeout" : "probe-failed", errorType = error.GetType().Name };
                    try { view?.Close(); } catch { }
                    view = new WebView2(); window.Content = view;
                }
                rows.Add(row);
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new { nonMessagingProbe = true, sentMessages = 0, filesAttached = false, navigationPerformed = true, sites = rows }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { nonMessagingProbe = true, errorType = error.GetType().Name, navigationPerformed = true, sites = rows }));
        }
        finally
        {
            try { view?.Close(); } catch { }
            try { window?.Close(); } catch { }
            QuitStandaloneAi();
        }
        await Task.CompletedTask;
    }

    static async Task<bool> DetectFileInput(WebView2 view, string[] fileNames)
    {
        string raw = await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
        { expression = DropWebScripts.FileInput(fileNames), returnByValue = false }));
        using var document = JsonDocument.Parse(raw);
        var result = document.RootElement.GetProperty("result");
        if (!result.TryGetProperty("objectId", out var objectId) || result.TryGetProperty("subtype", out var subtype) && subtype.GetString() == "null") return false;
        try { await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.releaseObject", JsonSerializer.Serialize(new { objectId = objectId.GetString() })); } catch { }
        return true;
    }
}
