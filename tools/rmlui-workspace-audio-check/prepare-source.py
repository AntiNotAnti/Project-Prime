"""Compile the exact production selector; all storage/native boundaries remain synthetic."""
from pathlib import Path
root=next(p for p in Path(__file__).resolve().parents if (p/'src/MphRead').exists())
source=(root/'src/MphRead/Mods/Launcher/RmlUi/Pages/theatre/TheatrePlaybackPagePresenter.Workspace.cs').read_text()
start=source.index('    private void SelectWorkspaceTab(string tab)')
end=source.index('    private bool HandleWorkspaceIntent',start)
body=source[start:end]
out=Path(__file__).resolve().parent/'obj/source-linked/WorkspaceSelector.cs'
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text('using System; using System.IO;\ninternal sealed partial class WorkspaceAudioHarness\n{\n'+body+'\n}\n')
