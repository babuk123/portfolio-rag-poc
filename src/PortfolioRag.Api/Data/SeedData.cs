using PortfolioRag.Api.Models;

namespace PortfolioRag.Api.Data;

/// <summary>Fictional sample customers and holdings for the POC (prices are static, not live).</summary>
public static class SeedData
{
    public static void Seed(AppDbContext db)
    {
        if (db.Customers.Any()) return;

        db.Customers.AddRange(
            new Customer
            {
                Id = "C1001", FullName = "Maria Thompson", Email = "maria.thompson@example.com",
                AccountNumberMasked = "XXXX-4821", RiskProfile = "Moderate", AdvisorName = "Alex Carter",
                Holdings =
                {
                    H("VTI",  "Vanguard Total Stock Market ETF", "Equity", 420, 198.50m, 301.20m),
                    H("AAPL", "Apple Inc.", "Equity", 150, 142.10m, 228.40m),
                    H("MSFT", "Microsoft Corp.", "Equity", 90, 255.00m, 468.75m),
                    H("BND",  "Vanguard Total Bond Market ETF", "Fixed Income", 600, 80.40m, 72.90m),
                    H("VTEB", "Vanguard Tax-Exempt Bond ETF", "Fixed Income", 300, 52.10m, 50.60m),
                    H("SPAXX","Fidelity Government Money Market", "Cash", 25000, 1.00m, 1.00m),
                }
            },
            new Customer
            {
                Id = "C1002", FullName = "Rajesh Iyer", Email = "rajesh.iyer@example.com",
                AccountNumberMasked = "XXXX-7733", RiskProfile = "Aggressive", AdvisorName = "Alex Carter",
                Holdings =
                {
                    H("QQQ",  "Invesco QQQ Trust", "Equity", 260, 310.00m, 512.30m),
                    H("NVDA", "NVIDIA Corp.", "Equity", 400, 45.20m, 141.80m),
                    H("VXUS", "Vanguard Total International Stock ETF", "Equity", 500, 55.30m, 66.10m),
                    H("VNQ",  "Vanguard Real Estate ETF", "Alternatives", 150, 92.00m, 89.40m),
                    H("SPAXX","Fidelity Government Money Market", "Cash", 8000, 1.00m, 1.00m),
                }
            },
            new Customer
            {
                Id = "C1003", FullName = "Linda Okafor", Email = "linda.okafor@example.com",
                AccountNumberMasked = "XXXX-1190", RiskProfile = "Conservative", AdvisorName = "Priya Shah",
                Holdings =
                {
                    H("BND",  "Vanguard Total Bond Market ETF", "Fixed Income", 1500, 79.80m, 72.90m),
                    H("SHY",  "iShares 1-3 Year Treasury Bond ETF", "Fixed Income", 700, 82.50m, 82.10m),
                    H("SCHD", "Schwab US Dividend Equity ETF", "Equity", 900, 25.10m, 28.70m),
                    H("SPAXX","Fidelity Government Money Market", "Cash", 60000, 1.00m, 1.00m),
                }
            });

        db.SaveChanges();
    }

    private static Holding H(string symbol, string name, string assetClass, decimal qty, decimal cost, decimal price) =>
        new() { Symbol = symbol, Name = name, AssetClass = assetClass, Quantity = qty, CostPerShare = cost, CurrentPrice = price };
}
