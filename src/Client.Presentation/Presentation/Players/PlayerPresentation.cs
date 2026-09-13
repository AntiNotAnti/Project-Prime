using System.Runtime.CompilerServices;

namespace MphRead.Entities
{
    public partial class PlayerPresentation : EntityPresentation, IPlayerPresentation
    {
        private readonly PlayerEntity _player;
        public ClientPlayerBindings Bindings { get; }

        internal PlayerPresentation(PlayerEntity player, ScenePresentation presentation) : base(player, presentation)
        {
            _player = player;
            Bindings = ClientPlayerBindings.GetDefault();
            Bindings.Attach(player.Controls);
            ApplyInputPreferences(player.Controls);
            player.Presentation = this;
        }
    }

    public static class PlayerPresentationExtensions
    {
        private static readonly ConditionalWeakTable<PlayerEntity, PlayerPresentation> Presentations = new();
        public static PlayerPresentation GetPresentation(this PlayerEntity player) => Presentations.GetValue(player, static p => new PlayerPresentation(p, ScenePresentation.Get(p._scene)));
    }
}
