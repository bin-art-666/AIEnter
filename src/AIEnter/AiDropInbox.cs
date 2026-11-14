using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;

namespace AIEnter;

internal sealed partial class AiWindow
{
    readonly StackPanel dropInbox = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    DropPayload? pendingDrop;
    string? pendingProvider, pendingEndpointKey, pendingTargetName;
    bool pendingFilesDelivered;
    bool transferringDrop;
    void InitializeDropInbox(Grid host)
    {
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var inboxScroll = new ScrollViewer { Content = dropInbox, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layout.SizeChanged += (_, _) => inboxScroll.MaxHeight = Math.Clamp(layout.ActualHeight * .55, 80, 300);
        layout.Children.Add(inboxScroll); Grid.SetRow(host, 1); layout.Children.Add(host);
        // Called before the host enters the visual tree.
        dropContent = layout;
        Closed += (_, _) => ClearDropInbox();
    }
    Grid? dropContent;
    internal async Task<bool> ReceiveDrop(DataPackageView view)
    {
        if (transferringDrop || ModalActive) return false;
        using var lease = KeepOpenForDrag();
        string provider = options.Data.Provider;
        var payload = await DropPayload.Read(view);
        return await ReceivePayload(payload, provider);
    }
    internal async Task<bool> ReceivePayload(DropPayload payload, string provider)
    {
        if (transferringDrop || ModalActive) { payload.Dispose(); return false; }
        using var lease = KeepOpen();
        bool retained = false;
        try
        {
            if (options.Data.Provider != provider) throw new IOException("接收期间 AI 已切换，请回到原 AI 后重新拖入。");
            if (payload.Bitmap != null)
            {
                string path = await ImageAttachments.Save(payload.Bitmap); payload.OwnFile(path); payload.Files = [path]; payload.Bitmap = null;
            }
            if (payload.Files.Length > 0 && !await Task.Run(() => payload.Files.All(File.Exists)).WaitAsync(TimeSpan.FromSeconds(5)))
                throw new IOException("文件已移动或无法访问，请检查原位置后重新拖入。");
            if (transferringDrop) return false;
            if (!IsOpen) ShowPanel();
            bool append = false;
            if (pendingDrop != null)
            {
                bool sameTarget = pendingProvider == options.Data.Provider && pendingEndpointKey == AiPolicy.Resolve(options.Data).Key;
                bool canAppend = sameTarget && pendingDrop.CanAppend(payload);
                var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = "已有待发送内容",
                    Content = $"当前有 {pendingDrop.Files.Length} 个文件、{pendingDrop.Text.Length} 字待交给 {pendingTargetName}。\n新拖入 {payload.Files.Length} 个文件、{payload.Text.Length} 字。"
                        + (!sameTarget ? "\n服务已改变，只能替换或取消，原暂存不会交给新服务。" : !canAppend ? "\n合并超过 12 个文件或 20000 字，可替换或取消。" : "\n追加会保留两次内容，替换会清除原暂存。")
                        + (pendingFilesDelivered ? "\n已有附件已交给网站；替换或取消不能撤回它们。" : ""),
                    PrimaryButtonText = "追加", IsPrimaryButtonEnabled = canAppend, SecondaryButtonText = "替换", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
                var choice = await dialog.ShowAsync();
                if (choice == ContentDialogResult.None) return false;
                append = choice == ContentDialogResult.Primary;
            }
            StageDrop(payload, append); retained = true;
            dropInbox.Visibility = Visibility.Collapsed; if (dropContent != null) dropContent.RowSpacing = 0;
            ConfirmIncomingDrop();
            if (current?.Ready != null) await current.Ready;
            await TransferDrop(); return true;
        }
        finally { if (!retained) payload.Dispose(); }
    }
    void StageDrop(DropPayload payload, bool append)
    {
        if (append)
        {
            if (pendingDrop == null || pendingProvider != options.Data.Provider || pendingEndpointKey != AiPolicy.Resolve(options.Data).Key)
                throw new IOException("待发送目标已变化，请取消后重新拖入。");
            pendingDrop.Append(payload);
        }
        else
        {
            ClearDropInbox(); pendingDrop = payload; pendingProvider = options.Data.Provider;
            var endpoint = AiPolicy.Resolve(options.Data); pendingEndpointKey = endpoint.Key; pendingTargetName = endpoint.Name;
        }
    }
    void RenderDropInbox()
    {
        if (pendingDrop is not { } payload) return;
        dropInbox.Children.Clear();
        dropInbox.Visibility = Visibility.Visible;
        if (dropContent != null) dropContent.RowSpacing = 8;
        dropInbox.Children.Add(new TextBlock { Text = "尚未放入 AI", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        dropInbox.Children.Add(new TextBlock { Text = payload.Text.Length > 300 ? payload.Text[..300] + "…" : payload.Text, MaxHeight = 80, TextWrapping = TextWrapping.Wrap });
        foreach (string file in payload.Files)
            dropInbox.Children.Add(new TextBlock { Text = Path.GetFileName(file), TextTrimming = TextTrimming.CharacterEllipsis });
        try
        {
            if (payload.Files.FirstOrDefault() is { } image && File.Exists(image) && new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" }.Contains(Path.GetExtension(image).ToLowerInvariant()) && new FileInfo(image).Length <= 32 * 1024 * 1024)
                dropInbox.Children.Add(new Image { Source = new BitmapImage(new Uri(image)) { DecodePixelWidth = 260 }, MaxHeight = 100, HorizontalAlignment = HorizontalAlignment.Left });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        dropInbox.Children.Add(new TextBlock { Text = "目标：" + pendingTargetName, FontSize = 12, Opacity = .7 });
        dropInbox.Children.Add(new TextBlock { Text = "网页尚未接收，请打开输入框或附件区域后重试。文件加入附件时网站可能开始上传，不自动发送消息。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .7 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var confirm = new Button { Content = "重试放入 " + pendingTargetName };
        confirm.Click += async (_, _) => { if (transferringDrop) return; confirm.IsEnabled = false; await TransferDrop(); confirm.IsEnabled = true; }; actions.Children.Add(confirm);
        if (payload.Text.Length > 0)
        {
            var copy = new Button { Content = "复制文字／链接" };
            copy.Click += (_, _) => { var data = new DataPackage(); data.SetText(payload.Text); Clipboard.SetContent(data); SetStatus("已复制，可粘贴到当前 AI 输入框。"); };
            actions.Children.Add(copy);
        }
        var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => { if (!transferringDrop) ClearDropInbox(); }; actions.Children.Add(cancel); dropInbox.Children.Add(actions);
    }
    void ClearDropInbox()
    {
        pendingDrop?.Dispose(); pendingDrop = null; pendingProvider = null; pendingEndpointKey = pendingTargetName = null;
        pendingFilesDelivered = false; if (dropContent != null) dropContent.RowSpacing = 0;
        dropInbox.Children.Clear(); dropInbox.Visibility = Visibility.Collapsed;
    }
    async Task TransferDrop()
    {
        if (pendingDrop is not { } payload || transferringDrop) return;
        transferringDrop = true; using var lease = KeepOpen();
        try
        {
            if (pendingProvider != options.Data.Provider || pendingEndpointKey != AiPolicy.Resolve(options.Data).Key || current?.View.CoreWebView2 is not { } core || current.Endpoint.Key != pendingEndpointKey || popup != null || current.Failed)
                throw new IOException("当前服务已变化或网页未就绪，请回到原服务，或取消后重新拖入。");
            if (!verification && (!Uri.TryCreate(core.Source, UriKind.Absolute, out var source) || source.Scheme != "https" || source.GetLeftPart(UriPartial.Authority) != new Uri(current.Endpoint.Url).GetLeftPart(UriPartial.Authority)))
                throw new IOException("请先完成登录并回到当前 AI 的对话页面，再确认。");
            if (payload.Files.Length > 0)
            {
                if (payload.Files.Any(p => !File.Exists(p))) throw new IOException("附件原位置已失效，请重新拖入。");
                string raw = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = DropWebScripts.FileInput(payload.Files), returnByValue = false }));
                using var doc = JsonDocument.Parse(raw); var node = doc.RootElement.GetProperty("result");
                if (!node.TryGetProperty("objectId", out var objectId) || node.TryGetProperty("subtype", out var subtype) && subtype.GetString() == "null")
                    throw new IOException("网页附件入口未就绪或存在多个入口。请打开网页的附件区域；如仍无法确定，请用网页附件按钮手动选择，暂存内容仍保留。");
                // The user explicitly dropped these files into this AI; attach without submitting a message.
                try
                {
                    await core.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles", JsonSerializer.Serialize(new { files = payload.Files, objectId = objectId.GetString() }));
                    payload.Commit(); payload.Files = []; pendingFilesDelivered = true;
                }
                finally { try { await core.CallDevToolsProtocolMethodAsync("Runtime.releaseObject", JsonSerializer.Serialize(new { objectId = objectId.GetString() })); } catch { } }
            }
            if (!string.IsNullOrWhiteSpace(payload.Text))
            {
                const string focus = DropWebScripts.FocusEditor;
                using var result = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = focus, returnByValue = true })));
                if (!result.RootElement.GetProperty("result").TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String) throw new IOException("网页输入框未就绪；请打开对话输入框后重试，文字仍保留。");
                await core.CallDevToolsProtocolMethodAsync("Input.insertText", JsonSerializer.Serialize(new { text = (value.GetString() == "newline" && !payload.Text.StartsWith("\n") ? "\n" : "") + payload.Text })); payload.Text = "";
            }
            ClearDropInbox(); SetStatus("已交给当前网站，文字放入草稿；请在网页中确认内容后发送。");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { RenderDropInbox(); SetStatus((pendingFilesDelivered ? "附件已交给网站；剩余步骤未完成：" : "未完成：") + ex.Message); }
        finally { transferringDrop = false; }
    }
}
