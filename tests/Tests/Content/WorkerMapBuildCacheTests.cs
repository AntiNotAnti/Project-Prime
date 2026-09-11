using System;
using System.Linq;
using MphRead.Mods.MapGen;
using ProjectPrime.Server.Worker.Maps;
using Xunit;

namespace MphRead.Tests.Content;

public sealed class WorkerMapBuildCacheTests
{
    [Fact]
    public void PreparedMapCacheIsBoundedAndDoesNotRetainMaterializedMounts()
    {
        Assert.InRange(WorkerMapBuildService.MaximumPreparedEntries, 1, 256);
        Type[] retainedTypes = typeof(WorkerMapBuildService.PreparedMapContent)
            .GetProperties().Select(property => property.PropertyType).ToArray();

        Assert.DoesNotContain(typeof(MapContentMount), retainedTypes);
        Assert.DoesNotContain(typeof(MatchContentSnapshot), retainedTypes);
        Assert.Contains(typeof(string), retainedTypes);
        Assert.Contains(typeof(MapDefinition), retainedTypes);
    }
}
