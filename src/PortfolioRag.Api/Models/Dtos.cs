namespace PortfolioRag.Api.Models;

public record CustomerListItem(string Id, string FullName, string RiskProfile, string AdvisorName, decimal TotalValue);

public record HoldingDto(string Symbol, string Name, string AssetClass, decimal Quantity,
    decimal CostPerShare, decimal CurrentPrice, decimal MarketValue, decimal GainLoss, decimal WeightPct);

public record AllocationDto(string AssetClass, decimal MarketValue, decimal WeightPct, decimal TargetPct);

public record PortfolioDto(string CustomerId, string FullName, string Email, string AccountNumberMasked,
    string RiskProfile, string AdvisorName, decimal TotalValue, decimal TotalCost, decimal TotalGainLoss,
    IReadOnlyList<HoldingDto> Holdings, IReadOnlyList<AllocationDto> Allocation);

public record CreateReviewRequest(string RequestedBy, string? AdvisorNotes);

public record ReviewDto(Guid Id, string CustomerId, string RequestedBy, string? AdvisorNotes, string Status,
    string? BlobName, int ChunksIndexed, string? Error, DateTime CreatedUtc);

public record ChatTurn(string Role, string Content);   // Role: "user" | "assistant"

public record ChatRequest(string CustomerId, string Question, List<ChatTurn>? History);

public record Citation(int Number, string DocumentName, int PageNumber, string BlobName, string DocType, string Snippet, double Score);

public record ChatResponse(string Answer, IReadOnlyList<Citation> Citations, bool Grounded, int ChunksRetrieved);

public static class Mapping
{
    public static ReviewDto ToDto(this ReviewRequest r) =>
        new(r.Id, r.CustomerId, r.RequestedBy, r.AdvisorNotes, r.Status, r.BlobName, r.ChunksIndexed, r.Error, r.CreatedUtc);
}
