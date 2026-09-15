using Xunit;

namespace ProjectPrime.Server.Node.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerProcessCollection
{
    public const string Name = "Worker process lifecycle";
}
