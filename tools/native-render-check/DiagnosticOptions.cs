using System;
using System.Linq;
using MphRead.Mods;

sealed partial class CheckWindow
{
    private int? _diagnosticScale;
    private TextureAssetQuality? _diagnosticTextureQuality;
    private bool? _diagnosticLighting;
    internal void ConfigureDiagnosticOptions(string[] args)
    {
        string Value(string key) => args[Array.IndexOf(args,key)+1];
        foreach(string key in new[]{"-render-scale","-texturequality","-lighting"})
            if(args.Contains(key) && Array.IndexOf(args,key)+1>=args.Length) throw new ArgumentException("Missing "+key+" value.");
        if(args.Contains("-render-scale"))
        {
            _diagnosticScale=int.Parse(Value("-render-scale"));
            if(_diagnosticScale<25 || _diagnosticScale>200) throw new ArgumentException("Render scale requires percent 25..200.");
        }
        if(args.Contains("-texturequality")) _diagnosticTextureQuality=Enum.Parse<TextureAssetQuality>(Value("-texturequality"),true);
        if(args.Contains("-lighting")) _diagnosticLighting=bool.Parse(Value("-lighting"));
    }
    private void ApplyDiagnosticOptions()
    {
        if(_diagnosticScale is int scale) RenderOptions.ResolutionScale=scale;
        if(_diagnosticTextureQuality is TextureAssetQuality quality) RenderOptions.TextureQuality=quality;
        if(_diagnosticLighting is bool lighting) RenderOptions.Lighting=lighting;
    }
}
