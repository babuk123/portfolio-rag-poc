namespace PortfolioRag.Api.Models;

public class Customer
{
    public string Id { get; set; } = "";              // e.g. C1001
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string AccountNumberMasked { get; set; } = "";
    public string RiskProfile { get; set; } = "Moderate"; // Conservative | Moderate | Aggressive
    public string AdvisorName { get; set; } = "";
    public List<Holding> Holdings { get; set; } = new();
    public List<ReviewRequest> Reviews { get; set; } = new();
}

public class Holding
{
    public int Id { get; set; }
    public string CustomerId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string AssetClass { get; set; } = "";       // Equity | Fixed Income | Cash | Alternatives
    public decimal Quantity { get; set; }
    public decimal CostPerShare { get; set; }
    public decimal CurrentPrice { get; set; }

    public decimal MarketValue => Quantity * CurrentPrice;
    public decimal CostBasis => Quantity * CostPerShare;
    public decimal GainLoss => MarketValue - CostBasis;
}

/// <summary>A financial advisor's request to produce a portfolio review PDF for a customer.</summary>
public class ReviewRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerId { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public string? AdvisorNotes { get; set; }
    public string Status { get; set; } = "Requested";  // Requested | Generated | Indexed | Failed
    public string? BlobName { get; set; }
    public int ChunksIndexed { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
