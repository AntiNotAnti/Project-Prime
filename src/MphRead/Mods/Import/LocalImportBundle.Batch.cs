using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.Import;

public sealed record ImportBundleUpdate(ImportBundleSpec Spec, IReadOnlyDictionary<string, byte[]> Components);

public sealed class ImportBundleBatchCanceledException : OperationCanceledException
{
    public IReadOnlyList<string> CommittedIds { get; }
    public ImportBundleBatchCanceledException(IEnumerable<string> committed, OperationCanceledException cause,
        CancellationToken cancellation) : base(MessageFor(committed), cause, cancellation)
    { CommittedIds = Array.AsReadOnly(committed.ToArray()); }
    private static string MessageFor(IEnumerable<string> ids) => "Local update cancelled. Already updated: "
        + string.Join(", ", ids) + ". Other bundles keep their previous receipts.";
}

public sealed class ImportBundleBatchException : IOException
{
    public IReadOnlyList<string> CommittedIds { get; }
    public ImportBundleBatchException(IEnumerable<string> committed, Exception cause)
        : base("Local update stopped. Already updated: " + string.Join(", ", committed)
            + ". Other bundles keep their previous receipts. " + cause.Message, cause)
    { CommittedIds = Array.AsReadOnly(committed.ToArray()); }
}

public static partial class LocalImportBundle
{
    /// <summary>Bounded, sequential independent transactions. Enumeration may
    /// prepare the next bundle lazily. Completed bundles are never rolled back
    /// if a later preparation, cancellation or publication fails.</summary>
    public static IReadOnlyList<string> PublishSequential(string root, IEnumerable<ImportBundleUpdate> updates,
        CancellationToken cancellation = default, IProgress<ImportPublishStage>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(updates);
        var committed = new List<string>();
        var files = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var update in updates)
            {
                cancellation.ThrowIfCancellationRequested();
                if (update == null || update.Spec == null || update.Components == null || !ids.Add(update.Spec.Id) || ids.Count > MaxComponents)
                    throw new InvalidDataException("A local update requires distinct bounded bundle identities.");
                var published = Publish(root, update.Spec, update.Components, cancellation, progress,
                    replaceAllComponents: true);
                committed.Add(update.Spec.Id);
                files.AddRange(published);
            }
            return files.AsReadOnly();
        }
        catch (OperationCanceledException ex) when (committed.Count > 0)
        { throw new ImportBundleBatchCanceledException(committed, ex, cancellation); }
        catch (Exception ex) when (committed.Count > 0 && ex is not OutOfMemoryException and not StackOverflowException)
        { throw new ImportBundleBatchException(committed, ex); }
    }
}
