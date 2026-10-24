namespace MphRead;

public partial class Scene
{
    // Scene ownership prevents a prior match/replica from retaining queued announcements.
    internal Mods.Sound.CtfAnnouncementAudio CtfAnnouncements { get; } = new();
}
