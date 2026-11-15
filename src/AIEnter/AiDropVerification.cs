using System.Text.Json;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AIEnter;

internal sealed partial class AiWindow
{
    async Task VerifyDropInputs()
    {
        var saved = options.Data;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
        async Task<string?> Text() => JsonSerializer.Deserialize<string>(await current!.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('draft').value"));
        async Task Ready()
        {
            await ShowSelected();
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (current!.View.CoreWebView2 is { } core && await core.ExecuteScriptAsync("!!document.getElementById('draft')") == "true") return;
                await Task.Delay(50);
            }
            throw new Exception("Drop fixture did not load.");
        }
        string area = Path.Combine(directory, "drop-fixtures"); Directory.CreateDirectory(area);
        string first = Path.Combine(area, "first.txt"), second = Path.Combine(area, "second.txt");
        await File.WriteAllTextAsync(first, "local attachment one"); await File.WriteAllTextAsync(second, "local attachment two");
        try
        {
            options.Save(options.Data with { Provider = "chatgpt", DefaultProvider = "deepseek" }); await Ready();
            Check(((FrameworkElement)Content).AllowDrop && current!.View.AllowDrop, "Native content or WebView drop routing is missing.");
            await current!.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('draft').value='existing draft';window.sent=0;let f=document.getElementById('attachments');if(!f){f=document.createElement('input');f.type='file';f.multiple=true;f.id='attachments';document.body.append(f);}const b=document.createElement('button');b.textContent='Send';b.onclick=()=>window.sent++;document.body.append(b);");
            var text = new DataPackage(); text.SetText("dropped text");
            Check(await ReceiveDrop(text.GetView()), "Text drop was rejected.");
            Check(await Text() == "existing draft\ndropped text", "Dropped text overwrote or failed to append the draft.");
            Check(Status.Contains("文字或链接已放入") && Status.Contains("尚未发送"), "Text receive did not report draft-only status.");
            ToggleDetached(); Check(Detached, "Drop protection prevented mode switching."); await Task.Delay(150);
            var link = new DataPackage(); link.SetWebLink(new Uri("https://example.invalid/link"));
            Check(await ReceiveDrop(link.GetView()) && (await Text())!.EndsWith("https://example.invalid/link"), "Expanded mode failed to receive links: " + await Text() + " | " + Status);
            Check(Status.Contains("文字或链接已放入"), "Link receive did not report successful placement.");
            var size = AppWindow.Size; AppWindow.Resize(new(size.Width - 40, size.Height - 20)); await Task.Delay(150);
            var files = new DataPackage(); files.SetStorageItems([await StorageFile.GetFileFromPathAsync(first), await StorageFile.GetFileFromPathAsync(second)]);
            await current!.View.CoreWebView2.ExecuteScriptAsync("window.dragOverlay=true;document.addEventListener('dragleave',()=>window.dragOverlay=false);document.addEventListener('dragend',()=>window.dragOverlay=false);");
            Check(await ReceiveDrop(files.GetView()), "Files were rejected after resize.");
            Check(await current!.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('attachments').files.length") == "2", "Files did not reach the page input.");
            Check(await current!.View.CoreWebView2.ExecuteScriptAsync("window.dragOverlay") == "false", "Page drag overlay lifecycle did not end after native delivery.");
            Check(Status.Contains("文件已加入网页附件") && Status.Contains("尚未发送"), "File receive did not report attachment-only status.");
            AppWindow.Resize(size); ToggleDetached(); Check(!Detached, "Drop protection prevented returning to floating mode."); await Task.Delay(150);
            string image = Path.Combine(area, "image.png");
            await File.WriteAllBytesAsync(image, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1cAAAAASUVORK5CYII="));
            var bitmap = new DataPackage(); bitmap.SetBitmap(RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(image)));
            Check(await ReceiveDrop(bitmap.GetView()), "Bitmap drop was rejected.");
            Check(await current!.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('attachments').files.length") == "1", "Bitmap did not become an attachment.");
            Check(Status.Contains("文件已加入网页附件"), "Image receive did not report successful attachment placement.");
            string queue = Path.Combine(directory, "incoming-drops"); Directory.CreateDirectory(queue);
            string packetFile = Path.Combine(queue, "local-test.json");
            await File.WriteAllTextAsync(packetFile, JsonSerializer.Serialize(new TaskbarDropPacket(Provider: "chatgpt", Text: "taskbar text", Files: [first])));
            var packet = await DropPayload.ReadPacket(packetFile, queue);
            Check(await ReceivePayload(packet.Payload, packet.Provider) && options.Data.Provider == "chatgpt", "Taskbar route changed current AI to the default.");
            Check((await Text())!.EndsWith("taskbar text"), "Taskbar text did not reach the current AI.");
            File.Delete(packetFile);
            string pictureBatch = Path.Combine(queue, "picture-test"); Directory.CreateDirectory(pictureBatch);
            string pictureSource = Path.Combine(pictureBatch, "image.source"); File.Copy(image, pictureSource, true);
            string picturePacket = Path.Combine(queue, "picture-test.json");
            await File.WriteAllTextAsync(picturePacket, JsonSerializer.Serialize(new TaskbarDropPacket(Provider: "chatgpt", Bitmap: pictureSource, OwnedFiles: [pictureSource])));
            var picture = await DropPayload.ReadPacket(picturePacket, queue);
            Check(await ReceivePayload(picture.Payload, picture.Provider), "Taskbar bitmap packet was rejected.");
            File.Delete(picturePacket);
            await current!.View.CoreWebView2.ExecuteScriptAsync("const doc=document.createElement('input');doc.type='file';doc.multiple=true;doc.accept='application/vnd.openxmlformats-officedocument.wordprocessingml.document';doc.id='word-files';document.body.append(doc)");
            Check(JsonSerializer.Deserialize<string>(await current!.View.CoreWebView2.ExecuteScriptAsync("(" + DropWebScripts.FileInput(["test.docx"]) + ")?.id")) == "word-files", "Typed attachment input selection failed.");
            await current!.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('word-files').remove()");
            await current!.View.CoreWebView2.ExecuteScriptAsync("const broad=document.createElement('input');broad.type='file';broad.multiple=true;broad.accept='application/*';broad.id='broad-files';document.body.append(broad)");
            Check(JsonSerializer.Deserialize<string>(await current!.View.CoreWebView2.ExecuteScriptAsync("(" + DropWebScripts.FileInput(["test.pdf"]) + ")?.id")) == "broad-files", "MIME wildcard attachment input was not recognized.");
            await current!.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('broad-files').remove()");
            await current!.View.CoreWebView2.ExecuteScriptAsync("const near=document.createElement('section');const ed=document.getElementById('draft');ed.before(near);near.append(ed);const local=document.createElement('input');local.type='file';local.multiple=true;local.id='composer-files';near.append(local);");
            Check(JsonSerializer.Deserialize<string>(await current!.View.CoreWebView2.ExecuteScriptAsync("(" + DropWebScripts.FileInput(["fixture.doc"]) + ")?.id")) == "composer-files", "Composer-local file input was not preferred over unrelated upload inputs.");
            await current!.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('composer-files').remove()");
            Check(await current!.View.CoreWebView2.ExecuteScriptAsync(DropWebScripts.AttachmentReceipt(["receipt-test.doc"])) == "false", "Missing UI attachment was reported received.");
            await current!.View.CoreWebView2.ExecuteScriptAsync("const receipt=document.createElement('div');receipt.id='receipt-test';receipt.textContent='receipt-test.doc';document.body.append(receipt)");
            Check(await current!.View.CoreWebView2.ExecuteScriptAsync(DropWebScripts.AttachmentReceipt(["receipt-test.doc"])) == "true", "Visible attachment receipt was not recognized.");
            await current!.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('receipt-test').textContent='receipt-test.doc 上传失败'");
            Check(await current!.View.CoreWebView2.ExecuteScriptAsync(DropWebScripts.AttachmentReceipt(["receipt-test.doc"])) == "false", "Failed upload was reported received.");
            await current!.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('receipt-test').remove()");
            string before = (await Text())!;
            bool rejected = false;
            try { await ReceivePayload(new DropPayload { Text = "wrong target" }, "deepseek"); } catch (IOException) { rejected = true; }
            Check(rejected && await Text() == before, "Drop crossed AI targets after selection changed.");
            await current!.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('attachments').remove()");
            var unsupported = new DataPackage(); unsupported.SetStorageItems([await StorageFile.GetFileFromPathAsync(first)]);
            Check(await ReceiveDrop(unsupported.GetView()) && pendingDrop != null && dropInbox.Visibility == Visibility.Visible, "Unsupported attachment lost retry content.");
            ClearDropInbox();
            Check(await current!.View.CoreWebView2.ExecuteScriptAsync("window.sent") == "0", "Drop submitted an AI message.");
        }
        finally { ClearDropInbox(); options.Save(saved); await ShowSelected(); }
    }
}
