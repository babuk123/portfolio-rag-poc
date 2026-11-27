using Microsoft.EntityFrameworkCore;
using PortfolioRag.Api.Data;
using PortfolioRag.Api.Models;

namespace PortfolioRag.Api.Services;

/// <summary>
/// Reads customers and holdings from SQL and values them. With MarketData keys configured, prices are live
/// (Finnhub quotes, cached 60s); otherwise the stored sample prices are used.
/// </summary>
public class PortfolioService(AppDbContext db, MarketDataService market)
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
        await market.ApplyLivePricesAsync(customers.SelectMany(c => c.Holdings), ct);   // cached, so cheap on repeat
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
        if (c is null) return null;
        var live = await market.ApplyLivePricesAsync(c.Holdings, ct);   // AsNoTracking above: never saved to SQL
        return Build(c, live);
    }

    public static PortfolioDto Build(Customer c, LivePricing? live = null)
    {
        StockQuote? Quote(Holding h) => live is not null && live.Quotes.TryGetValue(h.Symbol.ToUpperInvariant(), out var q) ? q : null;
        var total = c.Holdings.Sum(h => h.MarketValue);
        var cost = c.Holdings.Sum(h => h.CostBasis);
        decimal Pct(decimal v) => total == 0 ? 0 : Math.Round(v / total * 100, 1);

        var holdings = c.Holdings
            .OrderByDescending(h => h.MarketValue)
            .Select(h => new HoldingDto(h.Symbol, h.Name, h.AssetClass, h.Quantity, h.CostPerShare, h.CurrentPrice,
                Math.Round(h.MarketValue, 2), Math.Round(h.GainLoss, 2), Pct(h.MarketValue), Quote(h)?.ChangePct))
            .ToList();

        var targets = Targets.TryGetValue(c.RiskProfile, out var t) ? t : Targets["Moderate"];
        var allocation = targets.Keys
            .Select(ac =>
            {
                var mv = c.Holdings.Where(h => h.AssetClass == ac).Sum(h => h.MarketValue);
                return new AllocationDto(ac, Math.Round(mv, 2), Pct(mv), targets[ac]);
            })
            .ToList();

        // Today's change in dollars: shares x today's price change, for the holdings that have a live quote.
        decimal? dayChange = live is null ? null : Math.Round(c.Holdings.Sum(h => h.Quantity * (Quote(h)?.Change ?? 0)), 2);

        return new PortfolioDto(c.Id, c.FullName, c.Email, c.AccountNumberMasked, c.RiskProfile, c.AdvisorName,
            Math.Round(total, 2), Math.Round(cost, 2), Math.Round(total - cost, 2), holdings, allocation,
            live is null ? "Stored sample prices" : $"Live quotes ({live.Source})", live?.AsOfUtc, dayChange);
    }
}
