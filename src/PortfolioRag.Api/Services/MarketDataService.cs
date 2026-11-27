using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PortfolioRag.Api.Models;
using PortfolioRag.Api.Options;

namespace PortfolioRag.Api.Services;

/// <summary>Real month-end portfolio values for the 12-month chart.</summary>
public record PortfolioHistory(List<ValuePoint> Points, string Source);

/// <summary>
/// Live market data over HTTP:
///  - Finnhub     /quote        -> current price, today's change (used by the UI, the PDF and the chat tools)
///  - Twelve Data /time_series  -> month-end closing prices (used for the 12-month chart in the PDF)
/// Every call is cached (IMemoryCache) to stay inside the free quotas, and every failure returns null so the
/// caller falls back to the stored prices. A market-data outage must never break the advisor workflow.
/// API keys go in request headers, not the URL, so they never show up in logs.
/// </summary>
public partial class MarketDataService(
    IHttpClientFactory http, IMemoryCache cache, IOptions<MarketDataOptions> options, ILogger<MarketDataService> log)
{
    public const string FinnhubClient = "finnhub";
    public const string TwelveDataClient = "twelvedata";

    private MarketDataOptions O => options.Value;
    public bool QuotesEnabled => !string.IsNullOrWhiteSpace(O.FinnhubApiKey);
    public bool HistoryEnabled => !string.IsNullOrWhiteSpace(O.TwelveDataApiKey);

    // Tickers like AAPL, BRK.B, BF-B. Symbols can come from the LLM (tool arguments), so validate before use.
    [GeneratedRegex("^[A-Z][A-Z0-9.\\-]{0,9}$")]
    private static partial Regex TickerRegex();

    public static string? NormalizeSymbol(string? symbol)
    {
        var s = (symbol ?? "").Trim().ToUpperInvariant();
        return TickerRegex().IsMatch(s) ? s : null;
    }

    /// <summary>Money market funds trade at $1.00 and quote APIs usually don't cover them.</summary>
    public static bool IsCash(string assetClass) => assetClass.Equals("Cash", StringComparison.OrdinalIgnoreCase);

    // ---------------- Quotes (Finnhub) ----------------

    public async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken ct)
    {
        var s = NormalizeSymbol(symbol);
        if (s is null || !QuotesEnabled) return null;

        return await cache.GetOrCreateAsync($"quote:{s}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(O.QuoteCacheSeconds);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"quote?symbol={Uri.EscapeDataString(s)}");
                req.Headers.Add("X-Finnhub-Token", O.FinnhubApiKey);
                using var res = await http.CreateClient(FinnhubClient).SendAsync(req, ct);
                if (!res.IsSuccessStatusCode)
                {
                    log.LogWarning("Finnhub quote {Symbol} returned {Status}", s, (int)res.StatusCode);
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15);   // retry soon
                    return null;
                }

                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var r = doc.RootElement;
                var price = Dec(r, "c");
                if (price <= 0) return null;   // Finnhub answers unknown symbols with all zeros

                var t = r.TryGetProperty("t", out var te) && te.ValueKind == JsonValueKind.Number ? te.GetInt64() : 0;
                var asOf = t > 0 ? DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime : DateTime.UtcNow;
                return new StockQuote(s, price, Dec(r, "d"), Dec(r, "dp"), Dec(r, "pc"), Dec(r, "h"), Dec(r, "l"), asOf, "Finnhub");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                log.LogWarning(ex, "Finnhub quote {Symbol} failed", s);
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15);
                return null;
            }
        });
    }

    public async Task<Dictionary<string, StockQuote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        var distinct = symbols.Select(NormalizeSymbol).OfType<string>().Distinct().ToList();
        var quotes = await Task.WhenAll(distinct.Select(s => GetQuoteAsync(s, ct)));   // a handful of symbols per customer
        return quotes.OfType<StockQuote>().ToDictionary(q => q.Symbol);
    }

    /// <summary>
    /// Replaces CurrentPrice with the live price on each non-cash holding.
    /// Only call this on entities loaded with AsNoTracking, so the live prices are never saved to SQL by accident.
    /// </summary>
    public async Task<LivePricing?> ApplyLivePricesAsync(IEnumerable<Holding> holdings, CancellationToken ct)
    {
        if (!QuotesEnabled) return null;
        var priced = holdings.Where(h => !IsCash(h.AssetClass)).ToList();
        var quotes = await GetQuotesAsync(priced.Select(h => h.Symbol), ct);
        if (quotes.Count == 0) return null;

        foreach (var h in priced)
            if (quotes.TryGetValue(h.Symbol.ToUpperInvariant(), out var q))
                h.CurrentPrice = q.Price;

        return new LivePricing(quotes, quotes.Values.Max(q => q.AsOfUtc), "Finnhub");
    }

    // ---------------- History (Twelve Data) ----------------

    /// <summary>Month-end closing prices, oldest first. The last point is the current month (latest price).</summary>
    public async Task<List<ValuePoint>?> GetMonthlyClosesAsync(string symbol, int months, CancellationToken ct)
    {
        var s = NormalizeSymbol(symbol);
        if (s is null || !HistoryEnabled) return null;

        return await cache.GetOrCreateAsync($"history:{s}:{months}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(O.HistoryCacheHours);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"time_series?symbol={Uri.EscapeDataString(s)}&interval=1month&outputsize={months}");
                req.Headers.Add("Authorization", $"apikey {O.TwelveDataApiKey}");
                using var res = await http.CreateClient(TwelveDataClient).SendAsync(req, ct);
                using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var r = doc.RootElement;

                // Twelve Data returns HTTP 200 with {"status":"error","code":429,...} for quota and symbol errors.
                if (!res.IsSuccessStatusCode || r.TryGetProperty("status", out var st) && st.GetString() != "ok"
                    || !r.TryGetProperty("values", out var values))
                {
                    log.LogWarning("Twelve Data history {Symbol} failed: {Body}", s, r.ToString());
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                    return null;
                }

                return values.EnumerateArray()
                    .Select(v => new ValuePoint(
                        DateTime.ParseExact(v.GetProperty("datetime").GetString()![..7], "yyyy-MM", CultureInfo.InvariantCulture),
                        decimal.Parse(v.GetProperty("close").GetString()!, CultureInfo.InvariantCulture)))
                    .OrderBy(p => p.Month)
                    .ToList();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                          or FormatException or KeyNotFoundException or InvalidOperationException)
            {
                log.LogWarning(ex, "Twelve Data history {Symbol} failed", s);
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                return null;
            }
        });
    }

    /// <summary>
    /// Month-end value of the portfolio = sum of (shares today x that month's closing price), cash held flat.
    /// Assumes today's share quantities for the whole year (no transaction history in this POC).
    /// Returns null if any priced holding has no history, so the PDF falls back to the illustrative curve.
    /// </summary>
    public async Task<PortfolioHistory?> GetPortfolioHistoryAsync(PortfolioDto p, int months, CancellationToken ct)
    {
        if (!HistoryEnabled) return null;

        var cash = p.Holdings.Where(h => IsCash(h.AssetClass)).Sum(h => h.MarketValue);
        var byMonth = new Dictionary<DateTime, decimal>();
        HashSet<DateTime>? common = null;

        // Sequential on purpose: the free Twelve Data tier allows 8 calls per minute.
        foreach (var h in p.Holdings.Where(h => !IsCash(h.AssetClass)))
        {
            var closes = await GetMonthlyClosesAsync(h.Symbol, months, ct);
            if (closes is null || closes.Count == 0) return null;

            foreach (var c in closes)
                byMonth[c.Month] = byMonth.GetValueOrDefault(c.Month) + h.Quantity * c.Value;
            common = common is null ? closes.Select(c => c.Month).ToHashSet() : common.Intersect(closes.Select(c => c.Month)).ToHashSet();
        }
        if (common is null || common.Count < 2) return null;

        var points = common.Order().Select(m => new ValuePoint(m, byMonth[m] + cash)).ToList();
        points[^1] = points[^1] with { Value = p.TotalValue };   // current month = today's live value, matches the KPIs
        return new PortfolioHistory(points, "Twelve Data month-end closing prices");
    }

    private static decimal Dec(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;
}
