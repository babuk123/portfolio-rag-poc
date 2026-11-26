using Microsoft.AspNetCore.Mvc;
using PortfolioRag.Api.Models;
using PortfolioRag.Api.Services;

namespace PortfolioRag.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController(PortfolioService portfolios, ReviewService reviews) : ControllerBase
{
    [HttpGet]
    public Task<List<CustomerListItem>> List(CancellationToken ct) => portfolios.ListAsync(ct);

    [HttpGet("{customerId}")]
    public async Task<ActionResult<PortfolioDto>> Get(string customerId, CancellationToken ct) =>
        await portfolios.GetAsync(customerId, ct) is { } p ? Ok(p) : NotFound();

    [HttpGet("{customerId}/reviews")]
    public Task<List<ReviewDto>> Reviews(string customerId, CancellationToken ct) => reviews.ListAsync(customerId, ct);

    /// <summary>Advisor requests a portfolio review PDF for the customer.</summary>
    [HttpPost("{customerId}/reviews")]
    public async Task<ActionResult<ReviewDto>> CreateReview(string customerId, CreateReviewRequest body, CancellationToken ct)
    {
        var review = await reviews.CreateAsync(customerId, body, ct);
        if (review is null) return NotFound();
        return CreatedAtAction(nameof(ReviewsController.Download), "Reviews", new { reviewId = review.Id }, review);
    }
}
