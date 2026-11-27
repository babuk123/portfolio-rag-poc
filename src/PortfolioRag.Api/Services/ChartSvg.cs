using System.Globalization;
using System.Text;
using PortfolioRag.Api.Models;

namespace PortfolioRag.Api.Services;

public record ValuePoint(DateTime Month, decimal Value);

/// <summary>
/// Builds SVG charts for the review PDF (QuestPDF renders SVG natively).
/// Numbers in SVG must use invariant culture ("12.5", never "12,5").
/// </summary>
public static class ChartSvg
{
    public static readonly string[] Palette =
        { "#1F3A5F", "#2F6FDE", "#1FA971", "#E8673A", "#6B4FD1", "#E0A800", "#8A8F98", "#C2185B" };

    private const string Font = "Lato, Arial, sans-serif";
    private const string Navy = "#1F3A5F";

    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string K(double v) => "$" + (v / 1000).ToString("0", CultureInfo.InvariantCulture) + "K";

    /// <summary>
    /// POC ONLY: the database stores today's prices, not history. This builds a fixed, illustrative
    /// 12-month series that starts near cost basis and ends exactly at today's market value.
    /// Production: read month-end values from a daily valuation table.
    /// </summary>
    public static List<ValuePoint> SampleHistory(PortfolioDto p, DateTime asOfLocal, int months = 12)
    {
        var seed = p.CustomerId.Sum(c => c) % 7;          // same customer -> same curve every time
        double start = (double)p.TotalCost, end = (double)p.TotalValue;
        var firstMonth = new DateTime(asOfLocal.Year, asOfLocal.Month, 1).AddMonths(-(months - 1));

        var points = new List<ValuePoint>(months);
        for (var i = 0; i < months; i++)
        {
            var t = i / (double)(months - 1);
            var wiggle = Math.Sin(i * 1.3 + seed) * 0.025 * end * (1 - t);   // fades out, so the last point is exact
            points.Add(new ValuePoint(firstMonth.AddMonths(i), (decimal)(start + (end - start) * t + wiggle)));
        }
        return points;
    }

    /// <summary>Line chart of portfolio value with a dashed cost-basis reference line.</summary>
    public static string LineChart(float width, float height, IReadOnlyList<ValuePoint> pts, decimal costBasis)
    {
        double w = width, h = height, left = 54, right = 64, top = 12, bottom = 24;
        var values = pts.Select(x => (double)x.Value).Append((double)costBasis).ToList();
        double min = values.Min() * 0.97, max = values.Max() * 1.03;
        double X(int i) => left + i * (w - left - right) / (pts.Count - 1);
        double Y(double v) => top + (max - v) / (max - min) * (h - top - bottom);

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{N(w)}' height='{N(h)}' viewBox='0 0 {N(w)} {N(h)}' font-family='{Font}'>");

        // Gridlines + y labels (4 steps)
        for (var g = 0; g <= 4; g++)
        {
            var v = min + (max - min) * g / 4;
            var y = Y(v);
            sb.Append($"<line x1='{N(left)}' x2='{N(w - right)}' y1='{N(y)}' y2='{N(y)}' stroke='#E3E6EB' stroke-width='0.8'/>");
            sb.Append($"<text x='{N(left - 6)}' y='{N(y + 3)}' font-size='8' fill='#6B7280' text-anchor='end'>{K(v)}</text>");
        }

        // Area under the line, then the line itself
        var line = string.Join(" ", pts.Select((pt, i) => $"{N(X(i))},{N(Y((double)pt.Value))}"));
        sb.Append($"<polygon points='{N(X(0))},{N(h - bottom)} {line} {N(X(pts.Count - 1))},{N(h - bottom)}' fill='{Navy}' fill-opacity='0.08'/>");
        sb.Append($"<polyline points='{line}' fill='none' stroke='{Navy}' stroke-width='2' stroke-linejoin='round'/>");
        for (var i = 0; i < pts.Count; i++)
            sb.Append($"<circle cx='{N(X(i))}' cy='{N(Y((double)pts[i].Value))}' r='{(i == pts.Count - 1 ? 3.5 : 2)}' fill='{Navy}'/>");

        // Cost basis reference
        var yc = Y((double)costBasis);
        sb.Append($"<line x1='{N(left)}' x2='{N(w - right)}' y1='{N(yc)}' y2='{N(yc)}' stroke='#E8673A' stroke-width='1.2' stroke-dasharray='4 3'/>");
        sb.Append($"<text x='{N(w - right + 4)}' y='{N(yc + 3)}' font-size='8' fill='#E8673A'>Cost basis</text>");

        // Latest value label
        var last = pts[^1];
        sb.Append($"<text x='{N(w - right + 4)}' y='{N(Y((double)last.Value) + 3)}' font-size='8.5' font-weight='bold' fill='{Navy}'>{K((double)last.Value)}</text>");

        // X labels every other month
        for (var i = 0; i < pts.Count; i += 2)
            sb.Append($"<text x='{N(X(i))}' y='{N(h - 8)}' font-size='8' fill='#6B7280' text-anchor='middle'>{pts[i].Month.ToString("MMM yy", CultureInfo.InvariantCulture)}</text>");

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Pie chart of holdings by market value; percentages inside slices of 8% or more.</summary>
    public static string Pie(float size, IReadOnlyList<HoldingDto> holdings)
    {
        double s = size, cx = s / 2, cy = s / 2, r = s / 2 - 4;
        var total = holdings.Sum(x => (double)x.MarketValue);
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{N(s)}' height='{N(s)}' viewBox='0 0 {N(s)} {N(s)}' font-family='{Font}'>");

        if (total <= 0 || holdings.Count == 0)
        {
            sb.Append($"<circle cx='{N(cx)}' cy='{N(cy)}' r='{N(r)}' fill='#E3E6EB'/></svg>");
            return sb.ToString();
        }

        var angle = -Math.PI / 2;   // start at 12 o'clock
        for (var i = 0; i < holdings.Count; i++)
        {
            var share = (double)holdings[i].MarketValue / total;
            var sweep = share * 2 * Math.PI;
            var color = Palette[i % Palette.Length];

            if (share >= 0.9999)
                sb.Append($"<circle cx='{N(cx)}' cy='{N(cy)}' r='{N(r)}' fill='{color}'/>");
            else
            {
                double x1 = cx + r * Math.Cos(angle), y1 = cy + r * Math.Sin(angle);
                double x2 = cx + r * Math.Cos(angle + sweep), y2 = cy + r * Math.Sin(angle + sweep);
                var large = sweep > Math.PI ? 1 : 0;
                sb.Append($"<path d='M{N(cx)} {N(cy)} L{N(x1)} {N(y1)} A{N(r)} {N(r)} 0 {large} 1 {N(x2)} {N(y2)} Z' fill='{color}' stroke='#FFFFFF' stroke-width='1.5'/>");
            }

            if (share >= 0.08)
            {
                var mid = angle + sweep / 2;
                double lx = cx + r * 0.62 * Math.Cos(mid), ly = cy + r * 0.62 * Math.Sin(mid);
                sb.Append($"<text x='{N(lx)}' y='{N(ly + 3.5)}' font-size='10' font-weight='bold' fill='#FFFFFF' text-anchor='middle'>{(share * 100).ToString("0", CultureInfo.InvariantCulture)}%</text>");
            }
            angle += sweep;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
