using Microsoft.EntityFrameworkCore;
using PortfolioRag.Api.Data;
using PortfolioRag.Api.Models;

namespace PortfolioRag.Api.Services;

/// <summary>
/// The advisor workflow: request -> generate PDF -> store in Blob -> index for the chatbot.
/// Kept synchronous for the POC. In production: save the request, put a message on Service Bus,
/// return 202 Accepted, and let a worker do the rest (see README "Production design").
/// </summary>
public class ReviewService(
    AppDbContext db, PdfService pdf, BlobStorageService blobs, IngestionService ingestion, ILogger<ReviewService> log)
{
    public async Task<ReviewDto?> CreateAsync(string customerId, CreateReviewRequest input, CancellationToken ct)
    {
        var customer = await db.Customers.Include(c => c.Holdings).FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return null;

        var review = new ReviewRequest
        {
            CustomerId = customerId,
            RequestedBy = string.IsNullOrWhiteSpace(input.RequestedBy) ? customer.AdvisorName : input.RequestedBy,
            AdvisorNotes = input.AdvisorNotes
        };
        db.ReviewRequests.Add(review);
        await db.SaveChangesAsync(ct);

        try
        {
            var portfolio = PortfolioService.Build(customer);
            var bytes = pdf.CreatePortfolioReview(portfolio, review);

            review.BlobName = $"reviews/{customerId}/{review.CreatedUtc:yyyyMMdd-HHmmss}-{review.Id:N}.pdf";
            await blobs.UploadPdfAsync(review.BlobName, bytes, new Dictionary<string, string>
            {
                ["customerId"] = customerId,
                ["reviewId"] = review.Id.ToString()
            }, ct);
            review.Status = "Generated";
            await db.SaveChangesAsync(ct);

            var docName = $"Portfolio Review {customer.FullName} {review.CreatedUtc:yyyy-MM-dd}";
            review.ChunksIndexed = await ingestion.IndexPdfAsync(bytes, review.BlobName, docName, "portfolio-review", customerId, ct);
            review.Status = "Indexed";
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Review {Id} failed", review.Id);
            review.Status = "Failed";
            review.Error = ex.Message;
        }

        await db.SaveChangesAsync(ct);
        return review.ToDto();
    }

    public async Task<List<ReviewDto>> ListAsync(string customerId, CancellationToken ct) =>
        (await db.ReviewRequests.AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.CreatedUtc)
            .ToListAsync(ct))
        .Select(r => r.ToDto()).ToList();

    public Task<ReviewRequest?> GetAsync(Guid id, CancellationToken ct) =>
        db.ReviewRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
}
