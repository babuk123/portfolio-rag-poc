using Microsoft.AspNetCore.Mvc;
using PortfolioRag.Api.Models;
using PortfolioRag.Api.Services;

namespace PortfolioRag.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController(RagChatService rag) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Ask(ChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.CustomerId))
            return BadRequest("customerId and question are required.");
        if (request.Question.Length > 1000)
            return BadRequest("Question is too long.");

        return Ok(await rag.AskAsync(request, ct));
    }
}
