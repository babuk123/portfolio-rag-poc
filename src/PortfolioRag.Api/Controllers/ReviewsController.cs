using Microsoft.AspNetCore.Mvc;
using PortfolioRag.Api.Services;

namespace PortfolioRag.Api.Controllers;

[ApiController]
[Route("api")]
public class ReviewsController(ReviewService reviews, BlobStorageService blobs) : ControllerBase
{
    /// <summary>Streams the review PDF through the API (the blob container stays private).</summary>
    [HttpGet("reviews/{reviewId:guid}/pdf")]
    public async Task<IActionResult> Download(Guid reviewId, CancellationToken ct)
    {
        var review = await reviews.GetAsync(reviewId, ct);
        if (review?.BlobName is null) return NotFound();
        var bytes = await blobs.DownloadAsync(review.BlobName, ct);
        return bytes is null ? NotFound() : File(bytes, "application/pdf", Path.GetFileName(review.BlobName));
    }

    /// <summary>Opens any indexed file by blob name (used by chat citations). Only known folders are allowed.</summary>
    [HttpGet("files")]
    public async Task<IActionResult> GetFile([FromQuery] string blobName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(blobName) || blobName.Contains("..") ||
            !(blobName.StartsWith("reviews/") || blobName.StartsWith("policies/")))
            return BadRequest("Invalid file.");

        var bytes = await blobs.DownloadAsync(blobName, ct);
        return bytes is null ? NotFound() : File(bytes, "application/pdf");
    }
}
