using Microsoft.EntityFrameworkCore;
using PortfolioRag.Api.Data;
using PortfolioRag.Api.Models;

namespace PortfolioRag.Api.Services;

public class PortfolioService(AppDbContext db)
{
    /// <summary>Target allocation (%) per risk profile. The sample policy PDF uses the same numbers.</summary>
    public static readonly IReadOnlyDictionary<string, Dictionary<string, decimal>> Targets =
        new Dictionary<string, Dictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Conservative"] = new() { ["Equity"] = 30, ["Fixed Income"] = 50, ["Cash"] = 15, ["Alternatives"] = 5 },
            ["Moderate"]     = new() { ["Equity"] = 60, ["Fixed Income"] = 30, ["Cash"] = 7,  ["Alternatives"] = 3 },
            ["Aggressive"]   = new() { ["Equity"] = 80, ["Fixed Income"] = 10, ["Cash"] = 3,  ["Alternatives"] = 7 },
        };

    public async Task<List<CustomerListItem>> ListAsync(CancellationToken ct)
    {
        var customers = await db.Customers.Include(c => c.Holdings).AsNoTracking().ToListAsync(ct);
        return customers
            .Select(c => new CustomerListItem(c.Id, c.FullName, c.RiskProfile, c.AdvisorName,
                Math.Round(c.Holdings.Sum(h => h.MarketValue), 2)))
            .OrderBy(c => c.FullName)
            .ToList();
    }

    public async Task<PortfolioDto?> GetAsync(string customerId, CancellationToken ct)
    {
        var c = await db.Customers.Include(x => x.Holdings).AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == customerId, ct);
        return c is null ? null : Build(c);
    }

    public static PortfolioDto Build(Customer c)
    {
        var total = c.Holdings.Sum(h => h.MarketValue);
        var cost = c.Holdings.Sum(h => h.CostBasis);
        decimal Pct(decimal v) => total == 0 ? 0 : Math.Round(v / total * 100, 1);

        var holdings = c.Holdings
            .OrderByDescending(h => h.MarketValue)
            .Select(h => new HoldingDto(h.Symbol, h.Name, h.AssetClass, h.Quantity, h.CostPerShare, h.CurrentPrice,
                Math.Round(h.MarketValue, 2), Math.Round(h.GainLoss, 2), Pct(h.MarketValue)))
            .ToList();

        var targets = Targets.TryGetValue(c.RiskProfile, out var t) ? t : Targets["Moderate"];
        var allocation = targets.Keys
            .Select(ac =>
            {
                var mv = c.Holdings.Where(h => h.AssetClass == ac).Sum(h => h.MarketValue);
                return new AllocationDto(ac, Math.Round(mv, 2), Pct(mv), targets[ac]);
            })
            .ToList();

        return new PortfolioDto(c.Id, c.FullName, c.Email, c.AccountNumberMasked, c.RiskProfile, c.AdvisorName,
            Math.Round(total, 2), Math.Round(cost, 2), Math.Round(total - cost, 2), holdings, allocation);
    }
}
