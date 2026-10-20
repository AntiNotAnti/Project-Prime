using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

internal readonly record struct CustomFeedbackView(int FirePulse,int Empty,int Denied);
// Cosmetic fixed-step state only. No resource or camera/source-aim writes.
internal sealed class CustomWeaponFeedback
{
    private CustomInputProjection? _identity;
    private uint _fire,_latest;
    private int _pulse,_empty,_denied;
    internal CustomFeedbackView View=>new(_pulse,_empty,_denied);
    internal void Synchronize(CustomInputProjection? identity)
    {
        if(identity is not {} value||_identity is not {} old||old.Owner!=value.Owner
            ||old.Bootstrap!=value.Bootstrap||old.Manifest!=value.Manifest)Clear();
        _identity=identity;
    }
    internal void CancelCosmetics(){_pulse=_empty=_denied=0;}
    internal void Clear(){_fire=_latest=0;_pulse=_empty=_denied=0;}
    internal void Step(){if(_pulse>0)_pulse--;if(_empty>0)_empty--;if(_denied>0)_denied--;}
    internal void Submitted(CustomWeaponActionKind kind,uint sequence)
    {
        if(_identity==null)return;
        _latest=sequence;_empty=_denied=0;
        if(kind==CustomWeaponActionKind.Fire){_fire=sequence;_pulse=6;}
    }
    internal bool Apply(in CustomPresentationResult result)
    {
        if(_identity is not {} identity||identity.Owner!=result.Owner||identity.Bootstrap!=result.Bootstrap
            ||identity.Manifest!=result.Manifest)return false;
        if(result.Accepted)return false; // The local prediction already played.
        if(result.Kind==CustomWeaponActionKind.Fire&&result.Sequence==_fire)_pulse=0;
        // Older delayed results still settle their request but cannot replace
        // feedback for a newer physical action or replay an old dry-fire cue.
        if(result.Sequence<_latest)return false;
        if(result.Kind==CustomWeaponActionKind.Fire&&result.Result==WeaponActionResult.Empty)
        {_empty=30;_denied=0;return true;}
        _denied=30;return false;
    }
}
