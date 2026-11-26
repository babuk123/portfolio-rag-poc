using Microsoft.EntityFrameworkCore;
using PortfolioRag.Api.Models;

namespace PortfolioRag.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<ReviewRequest> ReviewRequests => Set<ReviewRequest>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Customer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(20);
            e.HasMany(x => x.Holdings).WithOne().HasForeignKey(h => h.CustomerId);
            e.HasMany(x => x.Reviews).WithOne().HasForeignKey(r => r.CustomerId);
        });

        b.Entity<Holding>(e =>
        {
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.CostPerShare).HasPrecision(18, 4);
            e.Property(x => x.CurrentPrice).HasPrecision(18, 4);
            e.Ignore(x => x.MarketValue);
            e.Ignore(x => x.CostBasis);
            e.Ignore(x => x.GainLoss);
        });

        b.Entity<ReviewRequest>(e => e.HasIndex(x => new { x.CustomerId, x.CreatedUtc }));
    }
}
