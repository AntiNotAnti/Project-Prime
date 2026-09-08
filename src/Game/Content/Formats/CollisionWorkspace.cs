using System.Collections.Generic;

namespace MphRead.Formats;

/// <summary>One query owns this scratch state and its returned candidates for their entire lifetime.</summary>
internal sealed class CollisionWorkspace
{
    internal List<CollisionCandidate> Candidates { get; } = new(64);
    // Preserve the original reverse room order followed by reverse entity order.
    internal Stack<CollisionCandidate> Pending { get; } = new(64);
}
