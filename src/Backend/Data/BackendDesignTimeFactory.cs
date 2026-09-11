using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Hosting;

namespace MphRead.Backend.Data;

// Explicit tooling entry: constructing the model never connects or migrates a database.
public sealed class BackendDesignTimeFactory : IDesignTimeDbContextFactory<BackendDbContext>
{
    public BackendDbContext CreateDbContext(string[] args)
    {
        string connection = Environment.GetEnvironmentVariable("ConnectionStrings__Backend")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Backend for migration tooling.");
        // Tooling runs outside the hosted DI container. Keep the same schema/history
        // provider settings and explicit bounded pool defaults without connecting here.
        var environment = new DesignTimeHostEnvironment();
        connection = BackendDatabase.ConfigureConnectionString(connection, environment);
        var options = new DbContextOptionsBuilder<BackendDbContext>();
        options.UseNpgsql(connection, BackendDatabase.ConfigureEf);
        return new BackendDbContext(options.Options);
    }

    private sealed class DesignTimeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ProjectPrime.Backend.Migrations";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
