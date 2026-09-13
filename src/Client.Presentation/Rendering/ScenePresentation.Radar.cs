using MphRead.Hud.Radar;

namespace MphRead;

public partial class ScenePresentation
{
    private readonly RadarMapPresentation _radarMapPresentation = new();
    public RadarMapPresentation RadarMapPresentation => _radarMapPresentation;
}
