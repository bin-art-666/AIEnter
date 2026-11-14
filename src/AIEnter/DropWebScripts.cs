using System.Text.Json;

namespace AIEnter;

internal static class DropWebScripts
{
    internal static string FileInput(string[] files)
    {
        string extensions = JsonSerializer.Serialize(files.Select(p => Path.GetExtension(p).ToLowerInvariant()).ToArray());
        return $$"""
        (()=>{
          const visible=e=>e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden';
          const editor=[...document.querySelectorAll('textarea:not([disabled]):not([readonly]),[contenteditable=true][role=textbox],[contenteditable=true]')].find(visible);
          if(!editor)return null;
          const extensions={{extensions}}, images=['.png','.jpg','.jpeg','.webp','.gif','.bmp','.avif','.heic','.heif','.tif','.tiff','.svg'];
          const mime={'.pdf':'application/pdf','.txt':'text/plain','.csv':'text/csv','.json':'application/json','.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg','.webp':'image/webp','.gif':'image/gif','.doc':'application/msword','.docx':'application/vnd.openxmlformats-officedocument.wordprocessingml.document','.xls':'application/vnd.ms-excel','.xlsx':'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet','.ppt':'application/vnd.ms-powerpoint','.pptx':'application/vnd.openxmlformats-officedocument.presentationml.presentation','.md':'text/markdown','.zip':'application/zip'};
          const candidates=[...document.querySelectorAll('input[type=file]:not([disabled])')].map(e=>{
            if(extensions.length>1&&!e.multiple)return {e,score:-1};
            const accept=e.accept.toLowerCase().split(',').map(s=>s.trim()).filter(Boolean);
            const accepts=ext=>accept.length===0||accept.some(a=>a==='*/*'||a===ext||a===mime[ext]||a==='image/*'&&images.includes(ext));
            if(!extensions.every(accepts))return {e,score:-1};
            return {e,score:(accept.length?30:10)+(e.multiple?1:0)+(e.closest('form')?.contains(editor)?5:0)-(e.hasAttribute('capture')?30:0)};
          }).filter(c=>c.score>=0).sort((a,b)=>b.score-a.score);
          if(!candidates.length||candidates.length>1&&candidates[0].score===candidates[1].score)return null;
          return candidates[0].e;
        })()
        """;
    }
    internal const string FocusEditor = """
        (()=>{
          const e=[...document.querySelectorAll('textarea:not([disabled]):not([readonly]),[contenteditable=true][role=textbox],[contenteditable=true]')]
            .find(e=>e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden');
          if(!e)return null;
          const text=e.value??e.innerText??'';
          e.focus();
          if(e.setSelectionRange)e.setSelectionRange(e.value.length,e.value.length);
          else{const r=document.createRange();r.selectNodeContents(e);r.collapse(false);const s=getSelection();s.removeAllRanges();s.addRange(r);}
          return text.length&&!text.endsWith('\n')?'newline':'empty';
        })()
        """;
}