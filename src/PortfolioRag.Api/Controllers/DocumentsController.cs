using Microsoft.AspNetCore.Mvc;
using PortfolioRag.Api.Services;

namespace PortfolioRag.Api.Controllers;

/// <summary>Firm-wide documents (policies) that every advisor's chatbot can search.</summary>
[ApiController]
[Route("api/documents")]
public class DocumentsController(PdfService pdf, BlobStorageService blobs, IngestionService ingestion) : ControllerBase
{
    /// <summary>Generates and indexes a sample "Portfolio Review and Rebalancing Policy" PDF.</summary>
    [HttpPost("sample-policy")]
    public async Task<IActionResult> SamplePolicy(CancellationToken ct)
    {
        var bytes = pdf.CreateSamplePolicy();
        const string blobName = "policies/WM-104-portfolio-review-policy.pdf";
        await blobs.UploadPdfAsync(blobName, bytes, null, ct);
        var chunks = await ingestion.IndexPdfAsync(bytes, blobName, "Policy WM-104 Portfolio Review and Rebalancing", "policy", "all", ct);
        return Ok(new { blobName, chunks });
    }

    /// <summary>Upload your own policy PDF (multipart/form-data, field name "file").</summary>
    [HttpPost("policy")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadPolicy(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0 || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Please upload a PDF.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var safeName = string.Concat(Path.GetFileName(file.FileName).Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-'));
        var blobName = $"policies/{safeName}";
        await blobs.UploadPdfAsync(blobName, bytes, null, ct);
        var chunks = await ingestion.IndexPdfAsync(bytes, blobName, Path.GetFileNameWithoutExtension(file.FileName), "policy", "all", ct);
        return Ok(new { blobName, chunks });
    }
}
