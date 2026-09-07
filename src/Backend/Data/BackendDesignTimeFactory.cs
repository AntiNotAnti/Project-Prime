using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MphRead.Backend.Data;

// Explicit tooling entry: constructing the model never connects or migrates a database.
public sealed class BackendDesignTimeFactory : IDesignTimeDbContextFactory<BackendDbContext>
{
    public BackendDbContext CreateDbContext(string[] args)
    {
        string connection = Environment.GetEnvironmentVariable("ConnectionStrings__Backend")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Backend for migration tooling.");
        return new BackendDbContext(new DbContextOptionsBuilder<BackendDbContext>().UseNpgsql(connection).Options);
    }
}
