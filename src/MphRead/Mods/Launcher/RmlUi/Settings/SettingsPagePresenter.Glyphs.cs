#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Text;

namespace MphRead.Mods.Launcher.RmlUi.Settings;
internal sealed partial class SettingsPagePresenter
{
    private const int GlyphsPerPage=24;
    private static readonly string[] NameGlyphs=Enumerable.Range(32,95).Select(i=>((char)i).ToString())
        .Concat(MphGlyphMap.Extended.Where(c=>c.Length==1&&c!=" ")).Distinct(StringComparer.Ordinal).ToArray();
    private bool _glyphOpen;
    private int _glyphCategory,_glyphPage;
    private static int GlyphCategory(string c)=>c[0]<127?0:c[0] is >= '\u3041' and <= '\u3096'?2
        :c[0] is >= '\u30a1' and <= '\u30ff'?3:c[0]<'\u3000'?1:4;
    private string[] CurrentGlyphs()=>NameGlyphs.Where(c=>GlyphCategory(c)==_glyphCategory).ToArray();
    private void OpenGlyphPicker()
    {
        if(_backend.RestartRequired||_controller.PendingVideoConfirmation||_modal!=default)return;
        string value=_controller.Draft["prefs.PlayerName"];
        if(_presented!=null)
            for(int i=0;i<_presented.Fields.Count;i++)
                if(_presented.Fields[i].Definition.Id=="prefs.PlayerName")value=_host.ReadField(_page,"settings_value_"+i);
        _glyphPage=_glyphCategory=0;
        _modal=_pages.OpenModal(new("settings-glyphs","pages/settings/glyphs.rml","settings_glyph_name"));_glyphOpen=true;
        _host.SetField(_modal,"settings_glyph_name",value);_host.FocusDocument(_modal,"settings_glyph_name");
    }
    private void HandleGlyphAction(in RmlUiIntent intent)
    {
        if(intent.Document!=_modal)return;
        if(intent.Kind==RmlUiIntentKind.SettingsClose){_glyphOpen=false;CloseModal();return;}
        if(intent.Kind==RmlUiIntentKind.SettingsApply)
        {
            string text=_host.ReadField(_modal,"settings_glyph_name");
            if(_controller.Set("prefs.PlayerName",text)){_glyphOpen=false;CloseModal();_resetNativeFields=true;}
            return;
        }
        if(intent.Kind==RmlUiIntentKind.SettingsCategory&&intent.Argument is >=0 and <5)
        {_glyphCategory=intent.Argument;_glyphPage=0;return;}
        if(intent.Kind!=RmlUiIntentKind.SettingsAction)return;
        string[] glyphs=CurrentGlyphs();int pages=Math.Max(1,(glyphs.Length+GlyphsPerPage-1)/GlyphsPerPage);
        if(intent.Argument is 57 or 58){_glyphPage=Math.Clamp(_glyphPage+(intent.Argument==57?-1:1),0,pages-1);return;}
        int index=intent.Argument is >=16 and <=37?intent.Argument-16:intent.Argument is 0 or 1?22+intent.Argument:-1;
        int glyph=_glyphPage*GlyphsPerPage+index;if(index<0||glyph>=glyphs.Length)return;
        string before=_host.ReadField(_modal,"settings_glyph_name");
        // RmlUi retains the input's selection while a glyph button owns focus.
        // ProcessTextInput performs the actual caret/selection insertion.
        _host.FocusDocument(_modal,"settings_glyph_name");_host.Input.Text(glyphs[glyph]);
        string after=_host.ReadField(_modal,"settings_glyph_name");
        if(PlayerNameCodec.ValidationError(after)!=null)_host.SetField(_modal,"settings_glyph_name",before);
    }
    private void PresentGlyphPicker()
    {
        if(!_glyphOpen||_modal==default)return;
        string[] glyphs=CurrentGlyphs();int pages=Math.Max(1,(glyphs.Length+GlyphsPerPage-1)/GlyphsPerPage);
        string name=_host.ReadField(_modal,"settings_glyph_name");
        var bindings=new Dictionary<string,RmlUiBindingValue>{
            ["settings_glyph_status"]=RmlUiBindingValue.FromText(PlayerNameCodec.ValidationError(name)??$"{PlayerNameCodec.GlyphCount(PlayerNameCodec.Normalize(name))} / {PlayerNameCodec.MaxGlyphs}"),
            ["settings_glyph_page"]=RmlUiBindingValue.FromText($"Page {_glyphPage+1} of {pages}"),
            ["disabled:settings_glyph_previous"]=RmlUiBindingValue.FromBoolean(_glyphPage==0),
            ["disabled:settings_glyph_next"]=RmlUiBindingValue.FromBoolean(_glyphPage+1>=pages)};
        for(int i=0;i<GlyphsPerPage;i++)
        {
            int index=_glyphPage*GlyphsPerPage+i;bool active=index<glyphs.Length;
            bindings["visible:settings_glyph_"+i]=RmlUiBindingValue.FromBoolean(active);
            if(active)bindings["settings_glyph_"+i]=RmlUiBindingValue.FromText(glyphs[index]==" "?"SPACE":glyphs[index]);
        }
        _pages.Present(_modal,++_bindingRevision,bindings);
    }
}
#endif
