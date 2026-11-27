namespace PortfolioRag.Api.Options;

public class AzureOpenAIOptions
{
    public const string Section = "AzureOpenAI";
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChatDeployment { get; set; } = "gpt-4.1-mini";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; set; } = 1536;
}

public class AzureSearchOptions
{
    public const string Section = "AzureSearch";
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string IndexName { get; set; } = "portfolio-docs";
    /// <summary>Number of chunks sent to the LLM.</summary>
    public int Top { get; set; } = 5;
    /// <summary>Semantic ranker needs Basic tier or higher.</summary>
    public bool UseSemanticRanker { get; set; } = false;
    /// <summary>Semantic reranker score is 0-4. Chunks below this are treated as "not relevant".</summary>
    public double MinRerankerScore { get; set; } = 1.5;
}

public class BlobStorageOptions
{
    public const string Section = "BlobStorage";
    /// <summary>"UseDevelopmentStorage=true" works with the Azurite emulator.</summary>
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";
    public string Container { get; set; } = "portfolio-docs";
}

/// <summary>Live market data. Leave the keys empty and the app falls back to the stored sample prices.</summary>
public class MarketDataOptions
{
    public const string Section = "MarketData";
    /// <summary>Finnhub (finnhub.io): live quotes. Free tier: 60 calls per minute.</summary>
    public string FinnhubApiKey { get; set; } = "";
    /// <summary>Twelve Data (twelvedata.com): monthly closing prices for the 12-month chart. Free tier: 8 calls per minute, 800 per day.</summary>
    public string TwelveDataApiKey { get; set; } = "";
    /// <summary>Quotes are cached so repeated screens and chat questions don't burn the free quota.</summary>
    public int QuoteCacheSeconds { get; set; } = 60;
    public int HistoryCacheHours { get; set; } = 12;
}
