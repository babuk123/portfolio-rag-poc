using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using PortfolioRag.Api.Options;

namespace PortfolioRag.Api.Services;

public class BlobStorageService
{
    private readonly Lazy<BlobContainerClient> _lazy;
    private BlobContainerClient _container => _lazy.Value;

    public BlobStorageService(IOptions<BlobStorageOptions> options)
    {
        var o = options.Value;
        _lazy = new Lazy<BlobContainerClient>(() =>
            new BlobServiceClient(o.ConnectionString).GetBlobContainerClient(o.Container));
    }

    /// <summary>Private container: files are only served through the API, never by public URL.</summary>
    public Task EnsureContainerAsync(CancellationToken ct = default) =>
        _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

    public async Task UploadPdfAsync(string blobName, byte[] bytes, IDictionary<string, string>? metadata, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobName);
        using var stream = new MemoryStream(bytes);
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/pdf" },
            Metadata = metadata
        }, ct);
    }

    public async Task<byte[]?> DownloadAsync(string blobName, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(blobName);
        if (!await blob.ExistsAsync(ct)) return null;
        var result = await blob.DownloadContentAsync(ct);
        return result.Value.Content.ToArray();
    }
}
