using MphRead.Hud;
using MphRead.Mods.Chat;
using OpenTK.Mathematics;
namespace MphRead.Mods.Training;
internal sealed class AimTrainerHud
{
    private readonly Scene _scene;
    private readonly HudObjectInstance _font = new(ChatFont.Cell, ChatFont.Cell);
    internal AimTrainerHud(Scene scene)
    {
        _scene = scene;
        _font.SetPaletteData(new ColorRgba[] { new(), new(255,255,255,255) }, scene);
        _font.SetCharacterData(ChatFont.Pixels, scene); _font.Enabled = true;
    }
    internal void Draw(AimTrainerSession session)
    {
        var s = session.Stats;
        _scene.DrawHudFlatBox(4, 30, 85, 94, new Vector4(.04f,.09f,.15f,.8f));
        string[] lines = { "AIM TRAINER", $"SCORE {s.Score}", session.Tracking ? $"TRACK {s.TrackingPercent:F1}%" : $"ACCURACY {s.Accuracy:F1}%",
            $"HEAD {s.HeadshotPercent:F1}%", $"STREAK {s.CurrentHitStreak}", session.Tracking ? $"LOCK {s.LongestContinuousTrack / 60.0:F2}S" : $"REACTION {s.AverageReactionMs:F0}MS",
            $"TIME {session.Definition.DurationSeconds - (int)s.ElapsedTime}", session.Feedback };
        float y = 33;
        foreach (string line in lines) { Text(7, y, line); y += 7; }
    }
    private void Text(float x, float y, string text)
    {
        const float scale = .5f;
        _font.Alpha = 1;
        foreach (char ch in text)
        {
            int glyph = ChatFont.Index(ch); if (glyph < 0) continue;
            _font.PositionX = x / 256; _font.PositionY = y / 192;
            _font.SetData(glyph, new ColorRgba(220, 243, 255, 255), _scene);
            _scene.DrawHudObject(_font, mode: 1, scale: scale); x += ChatFont.Widths[glyph] * scale;
        }
    }
}
