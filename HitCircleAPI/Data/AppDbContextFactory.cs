using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HitCircleAPI.Data;

/// <summary>Allows EF tooling to scaffold migrations without starting the API or connecting to Redis.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(
                "Server=localhost;Database=hitcircleapi;User=design_time;Password=design_time",
                new MySqlServerVersion(new Version(8, 0, 0)))
            .Options;

        return new AppDbContext(options);
    }
}
