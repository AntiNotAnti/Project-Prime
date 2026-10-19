from pathlib import Path
root=next(p for p in Path(__file__).resolve().parents if (p/'src/MphRead').exists())
out=Path(__file__).resolve().parent/'obj/source-linked';out.mkdir(parents=True,exist_ok=True)
s=(root/'src/MphRead/Mods/Network/DemoPlayback.cs').read_text();a=s.index('    private static readonly ConditionalWeakTable<object, object> PlaybackOwnerTokens');b=s.index('    internal static Scene? ReplicaScene',a)
(out/'PlaybackOwner.cs').write_text('using System.Runtime.CompilerServices; namespace MphRead.Mods.Network { internal static partial class DemoPlayback {\n'+s[a:b]+'\n}}')
