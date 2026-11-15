using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QuickCommands.Server.Options;

namespace QuickCommands.Server.Data;

/// <summary>Used by "dotnet ef migrations add" – needs no running database because the server version is fixed.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql("Server=localhost;Database=quickcommands", ServerVersion.Parse(new DatabaseOptions().ServerVersion))
            .Options;
        return new AppDbContext(options);
    }
}
