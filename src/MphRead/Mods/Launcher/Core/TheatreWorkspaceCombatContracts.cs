#if !MPHREAD_SERVER
using System.Collections.Immutable;
namespace MphRead.Mods.Launcher.Core;
public sealed record TheatreCombatChoice(string Label, string Detail, bool HasFire);
public sealed record TheatreCombatReview(TheatreEditorContext Context, ImmutableArray<TheatreCombatChoice> Shots,
    int Selected, bool ShooterView, object Evidence);
/// <summary>Read-only presentation commands over exact retained authoritative shot evidence.</summary>
public interface ITheatreWorkspaceCombatBackend
{
    TheatreCombatReview ReadCombat(TheatreEditorContext context);
    void SelectCombat(TheatreCombatReview review, int index);
    void ShowCombat(TheatreCombatReview review, bool shooterView);
}
#endif
