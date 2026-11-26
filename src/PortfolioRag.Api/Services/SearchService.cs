using System.Text.Json.Serialization;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using PortfolioRag.Api.Options;

namespace PortfolioRag.Api.Services;

/// <summary>One searchable chunk in Azure AI Search.</summary>
public class ChunkDocument
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("contentVector")] public float[]? ContentVector { get; set; }
    /// <summary>"all" for firm-wide documents (policies), otherwise the customer ID. Used for security filtering.</summary>
    [JsonPropertyName("customerId")] public string CustomerId { get; set; } = "all";
    [JsonPropertyName("docType")] public string DocType { get; set; } = "";       // portfolio-review | policy
    [JsonPropertyName("documentName")] public string DocumentName { get; set; } = "";
    [JsonPropertyName("blobName")] public string BlobName { get; set; } = "";
    [JsonPropertyName("pageNumber")] public int PageNumber { get; set; }
    [JsonPropertyName("createdUtc")] public DateTimeOffset CreatedUtc { get; set; }
}

public record SearchHit(ChunkDocument Doc, double Score, double? RerankerScore);

public class SearchService
{
    private const string VectorProfile = "vector-profile";
    private const string HnswConfig = "hnsw-config";
    private const string SemanticConfig = "semantic-config";

    private readonly Lazy<SearchIndexClient> _lazyIndexClient;
    private SearchIndexClient _indexClient => _lazyIndexClient.Value;
    private SearchClient _searchClient => _lazyIndexClient.Value.GetSearchClient(_search.IndexName);
    private readonly AzureSearchOptions _search;
    private readonly int _dimensions;

    public SearchService(IOptions<AzureSearchOptions> search, IOptions<AzureOpenAIOptions> openAi)
    {
        _search = search.Value;
        _dimensions = openAi.Value.EmbeddingDimensions;
        // Created on first use, so the app still starts before Azure settings are filled in.
        _lazyIndexClient = new Lazy<SearchIndexClient>(() =>
            new SearchIndexClient(new Uri(_search.Endpoint), new AzureKeyCredential(_search.ApiKey)));
    }

    /// <summary>Creates or updates the index: keyword fields + a vector field + filterable metadata.</summary>
    public async Task EnsureIndexAsync(CancellationToken ct = default)
    {
        var index = new SearchIndex(_search.IndexName)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    VectorSearchDimensions = _dimensions,
                    VectorSearchProfileName = VectorProfile
                },
                new SimpleField("customerId", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("docType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                new SimpleField("documentName", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("blobName", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("pageNumber", SearchFieldDataType.Int32),
                new SimpleField("createdUtc", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
            },
            VectorSearch = new VectorSearch
            {
                Profiles = { new VectorSearchProfile(VectorProfile, HnswConfig) },
                Algorithms = { new HnswAlgorithmConfiguration(HnswConfig) }
            }
        };

        if (_search.UseSemanticRanker)
        {
            index.SemanticSearch = new SemanticSearch
            {
                Configurations =
                {
                    new SemanticConfiguration(SemanticConfig, new SemanticPrioritizedFields
                    {
                        ContentFields = { new SemanticField("content") }
                    })
                }
            };
        }

        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: ct);
    }

    public async Task UploadAsync(IEnumerable<ChunkDocument> docs, CancellationToken ct) =>
        await _searchClient.MergeOrUploadDocumentsAsync(docs, cancellationToken: ct);

    /// <summary>
    /// Hybrid search = keyword (BM25) + vector similarity, merged with Reciprocal Rank Fusion.
    /// The filter is the security trim: an advisor asking about customer X only sees X's documents and firm policies.
    /// </summary>
    public async Task<List<SearchHit>> HybridSearchAsync(string question, float[] questionVector, string customerId, CancellationToken ct)
    {
        var safeId = customerId.Replace("'", "''");
        var options = new SearchOptions
        {
            Size = _search.Top,
            Filter = $"customerId eq '{safeId}' or customerId eq 'all'",
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(questionVector)
                    {
                        KNearestNeighborsCount = 50,
                        Fields = { "contentVector" }
                    }
                }
            }
        };
        foreach (var f in new[] { "id", "content", "customerId", "docType", "documentName", "blobName", "pageNumber", "createdUtc" })
            options.Select.Add(f);

        if (_search.UseSemanticRanker)
        {
            options.QueryType = SearchQueryType.Semantic;
            options.SemanticSearch = new SemanticSearchOptions { SemanticConfigurationName = SemanticConfig };
        }

        SearchResults<ChunkDocument> results = await _searchClient.SearchAsync<ChunkDocument>(question, options, ct);

        var hits = new List<SearchHit>();
        await foreach (var r in results.GetResultsAsync())
            hits.Add(new SearchHit(r.Document, r.Score ?? 0, r.SemanticSearch?.RerankerScore));
        return hits;
    }
}
