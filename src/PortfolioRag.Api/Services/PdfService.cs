using PortfolioRag.Api.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PortfolioRag.Api.Services;

/// <summary>Creates PDFs with QuestPDF. The text in these PDFs is what the RAG chatbot later answers from.</summary>
public class PdfService
{
    private const string Navy = "#1F3A5F";

    // Servers run on UTC; show dates in the firm's local time (Windows and Linux both accept this IANA id in .NET 6+).
    private static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), BusinessTimeZone);

    static PdfService() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] CreatePortfolioReview(PortfolioDto p, ReviewRequest req)
    {
        var drift = p.Allocation.Select(a => (a, diff: a.WeightPct - a.TargetPct)).ToList();
        var needsRebalance = drift.Where(d => Math.Abs(d.diff) > 5).ToList();

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(col =>
            {
                col.Item().Text("Contoso Wealth Management").FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().Text("Portfolio Review").FontSize(20).Bold().FontColor(Navy);
                col.Item().Text($"Prepared for {p.FullName} on {ToLocal(req.CreatedUtc):MMMM d, yyyy}").FontSize(11);
                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Navy);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(10);

                // 1. Client summary
                col.Item().Text("1. Client summary").FontSize(13).Bold().FontColor(Navy);
                col.Item().Text(t =>
                {
                    t.Line($"Client: {p.FullName} (ID {p.CustomerId})");
                    t.Line($"Account: {p.AccountNumberMasked}");
                    t.Line($"Risk profile: {p.RiskProfile}");
                    t.Line($"Financial advisor: {p.AdvisorName}");
                    t.Line($"Review requested by: {req.RequestedBy}");
                });

                // 2. Portfolio value
                col.Item().Text("2. Portfolio value").FontSize(13).Bold().FontColor(Navy);
                col.Item().Text(t =>
                {
                    t.Line($"Total market value: {p.TotalValue:C}");
                    t.Line($"Total cost basis: {p.TotalCost:C}");
                    var pct = p.TotalCost == 0 ? 0 : p.TotalGainLoss / p.TotalCost * 100;
                    t.Line($"Unrealized gain/loss: {p.TotalGainLoss:C} ({pct:F1}%)");
                });

                // 3. Asset allocation vs target
                col.Item().Text("3. Asset allocation vs target").FontSize(13).Bold().FontColor(Navy);
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                    table.Header(h =>
                    {
                        foreach (var title in new[] { "Asset class", "Market value", "Actual %", "Target %", "Drift" })
                            h.Cell().Background(Navy).Padding(4).Text(title).FontColor(Colors.White).Bold();
                    });
                    foreach (var (a, diff) in drift)
                    {
                        table.Cell().BorderBottom(0.5f).Padding(4).Text(a.AssetClass);
                        table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text($"{a.MarketValue:C}");
                        table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text($"{a.WeightPct:F1}%");
                        table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text($"{a.TargetPct:F1}%");
                        table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text($"{diff:+0.0;-0.0;0.0} pts");
                    }
                });

                // 4. Holdings
                col.Item().Text("4. Holdings").FontSize(13).Bold().FontColor(Navy);
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(4); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(); });
                    table.Header(h =>
                    {
                        foreach (var title in new[] { "Symbol", "Name", "Asset class", "Market value", "Gain/loss", "Weight" })
                            h.Cell().Background(Navy).Padding(4).Text(title).FontColor(Colors.White).Bold();
                    });
                    foreach (var h in p.Holdings)
                    {
                        table.Cell().BorderBottom(0.5f).Padding(4).Text(h.Symbol);
                        table.Cell().BorderBottom(0.5f).Padding(4).Text(h.Name);
                        table.Cell().BorderBottom(0.5f).Padding(4).Text(h.AssetClass);
                        table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text($"{h.MarketValue:C}");
                        table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text($"{h.GainLoss:C}");
                        table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text($"{h.WeightPct:F1}%");
                    }
                });

                // 5. Observations (written as full sentences so the chatbot can retrieve them)
                col.Item().Text("5. Observations and recommendations").FontSize(13).Bold().FontColor(Navy);
                col.Item().Text(t =>
                {
                    if (needsRebalance.Count == 0)
                        t.Line($"All asset classes are within 5 percentage points of the {p.RiskProfile} target, so no rebalancing is needed at this review.");
                    foreach (var (a, diff) in needsRebalance)
                        t.Line($"{a.AssetClass} is {(diff > 0 ? "overweight" : "underweight")} by {Math.Abs(diff):F1} percentage points " +
                               $"({a.WeightPct:F1}% actual vs {a.TargetPct:F1}% target). Rebalancing is recommended under the drift policy.");

                    var top = p.Holdings.FirstOrDefault();
                    if (top is not null && top.WeightPct > 20)
                        t.Line($"Concentration: {top.Symbol} is {top.WeightPct:F1}% of the portfolio, above the 20% single-holding guideline.");

                    var losers = p.Holdings.Where(h => h.GainLoss < 0).Select(h => h.Symbol).ToList();
                    if (losers.Count > 0)
                        t.Line($"Holdings with unrealized losses that may be reviewed for tax-loss harvesting: {string.Join(", ", losers)}.");
                });

                if (!string.IsNullOrWhiteSpace(req.AdvisorNotes))
                {
                    col.Item().Text("6. Advisor notes").FontSize(13).Bold().FontColor(Navy);
                    col.Item().Text(req.AdvisorNotes);
                }

            });

            // Disclosure lives in the footer so it never spills onto an extra page.
            page.Footer().Column(f =>
            {
                f.Item().Text(
                    "Disclosures: This review is for discussion purposes only and is not a recommendation to buy or sell any security. " +
                    "Values are as of the review date and are not guaranteed. Past performance does not guarantee future results.")
                    .FontSize(7).FontColor(Colors.Grey.Darken1);
                f.Item().PaddingTop(4).AlignCenter().Text(t =>
                {
                    t.Span("Confidential | Page ").FontSize(8);
                    t.CurrentPageNumber().FontSize(8);
                    t.Span(" of ").FontSize(8);
                    t.TotalPages().FontSize(8);
                });
            });
        })).GeneratePdf();
    }

    /// <summary>A sample bank policy document so the chatbot can combine customer data with firm rules.</summary>
    public byte[] CreateSamplePolicy()
    {
        var sections = new (string Title, string Body)[]
        {
            ("1. Purpose", "This policy sets the rules financial advisors follow when reviewing client investment portfolios at Contoso Wealth Management."),
            ("2. Review frequency", "Advisors must review every client portfolio at least once every 12 months. Clients with a portfolio value above $1,000,000 must be reviewed every 6 months. A review is also required within 30 days after a client changes their risk profile."),
            ("3. Target allocation by risk profile", "Conservative: 30% Equity, 50% Fixed Income, 15% Cash, 5% Alternatives. Moderate: 60% Equity, 30% Fixed Income, 7% Cash, 3% Alternatives. Aggressive: 80% Equity, 10% Fixed Income, 3% Cash, 7% Alternatives."),
            ("4. Rebalancing rule", "Rebalancing is recommended when any asset class drifts more than 5 percentage points from its target. Rebalancing must be discussed with the client and documented before trades are placed."),
            ("5. Concentration limit", "No single holding should exceed 20% of a client's portfolio. Exceptions require written client acknowledgement and supervisor approval."),
            ("6. Fees", "The standard advisory fee is 0.85% per year on the first $1,000,000 and 0.60% per year above $1,000,000, billed quarterly in arrears. Fees are waived for cash held in money market funds."),
            ("7. Tax-loss harvesting", "Advisors may suggest selling holdings with unrealized losses to offset gains. The wash-sale rule must be respected: the same or a substantially identical security cannot be bought within 30 days before or after the sale."),
            ("8. Client communication", "Portfolio review PDFs must be delivered through the secure client portal. Advisors must not send account numbers or full statements by regular email."),
        };

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(10));
            page.Header().Column(col =>
            {
                col.Item().Text("Contoso Wealth Management").FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().Text("Portfolio Review and Rebalancing Policy").FontSize(18).Bold().FontColor(Navy);
                col.Item().Text("Policy WM-104, effective January 1, 2026").FontSize(10);
                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Navy);
            });
            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(8);
                foreach (var (title, body) in sections)
                {
                    col.Item().Text(title).FontSize(12).Bold().FontColor(Navy);
                    col.Item().Text(body);
                }
            });
            page.Footer().AlignCenter().Text(t => { t.Span("Internal use | Page ").FontSize(8); t.CurrentPageNumber().FontSize(8); });
        })).GeneratePdf();
    }
}
