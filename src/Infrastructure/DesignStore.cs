using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SolarInsight.Infrastructure;

public sealed class DesignStore : IDesignTimeDbContextFactory<Store>
{
    public Store CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<Store>().UseNpgsql("Host=localhost;Database=solar_design").Options);
}

