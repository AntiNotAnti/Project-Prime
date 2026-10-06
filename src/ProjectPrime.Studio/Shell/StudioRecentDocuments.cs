namespace ProjectPrime.Studio.Shell;

public sealed record StudioRecentDocument(StudioDocumentKind Kind, string Path, DateTimeOffset LastOpenedUtc);

public sealed class StudioRecentDocuments
{
    private readonly List<StudioRecentDocument> _items;
    public IReadOnlyList<StudioRecentDocument> Items => _items;
    public StudioRecentDocuments(IEnumerable<StudioRecentDocument> initial) => _items = initial.Take(20).ToList();
    public void Add(IStudioDocument document)
    {
        if (document.Path is not { } path) return;
        _items.RemoveAll(item => string.Equals(item.Path, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        _items.Insert(0, new(document.Kind, path, DateTimeOffset.UtcNow));
        if (_items.Count > 20) _items.RemoveRange(20, _items.Count - 20);
    }
    public void Clear() => _items.Clear();
}
