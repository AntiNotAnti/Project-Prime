namespace MphRead;

public partial class ScenePresentation
{
    /// <summary>Facts delivered to this presentation's scene, never another live session.</summary>
    public BroadcastObservationJournal BroadcastObservations { get; } = new();
}
