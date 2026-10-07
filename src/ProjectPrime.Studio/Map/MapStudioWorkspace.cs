using Avalonia.Controls;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Map;

public sealed class MapStudioWorkspace : UserControl
{
    public MapStudioWorkspace(IStudioDocument document)
    {
        Content = StudioWorkspaceView.Create(document, "Map Studio", "Create a new map from Studio Home, or open a map project to use the shared authoring tools.");
    }
}
