using System.Globalization;
using System.Text;
using PropertyData.CapeTown.Internal;
using PropertyData.Core.Models;

namespace PropertyData.AreaMaps;

/// <summary>A parcel on the map: its erf (to match sales), street number (to label) and outline.</summary>
public sealed record MapParcel(string? Erf, string? Number, Ring Ring);

/// <summary>A road centreline: name (to label), width in metres (to draw) and line.</summary>
public sealed record MapRoad(string Name, string? Type, double? WidthM, IReadOnlyList<LatLng> Line);

/// <summary>A comparable sale on the map, numbered as in the report's table.</summary>
public sealed record MapSale(int Number, string? Erf, LatLng Location);

public sealed record AreaMapInput(
    LatLng Centre,
    Ring? Subject,
    string? SubjectErf,
    IReadOnlyList<MapParcel> Parcels,
    IReadOnlyList<MapRoad> Roads,
    IReadOnlyList<MapSale> Sales,
    double ExtentM,
    double? RadiusM,
    int WidthPx,
    int HeightPx,
    string Source,
    bool Block = false);

/// <summary>
/// Draws the neighbourhood as an SVG map from municipal open data: every erf with its street
/// number, the roads with their names, the property in red, the comparable sales as numbered blue
/// erven and the radius they were drawn from. Like the site plan, it is our own drawing, so it
/// prints freely (the City of Cape Town's open data terms ask for the source line it carries).
/// </summary>
public static class AreaMapRenderer
{
    private const string Paper = "#EDF1F5";
    private const string ParcelFill = "#FFFFFF";
    private const string ParcelStroke = "#C8D1DB";
    private const string NumberInk = "#8A95A1";
    private const string RoadInk = "#55616D";
    private const string MainRoad = "#F4D67C";
    private const string SaleFill = "#3D74E0";
    private const string SaleStroke = "#1F4FB5";
    private const string SubjectFill = "#E0433A";
    private const string SubjectStroke = "#A8231C";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Render(AreaMapInput m)
    {
        double w = m.WidthPx, h = m.HeightPx;
        var scale = Math.Min(w, h) / (2 * m.ExtentM);             // px per metre
        (double X, double Y) Px(LatLng p)
        {
            var (x, y) = Geo.ToLocalMetres(p, m.Centre);
            return (w / 2 + x * scale, h / 2 - y * scale);
        }
        bool OnMap(IEnumerable<(double X, double Y)> pts) =>
            pts.Any(p => p.X > -20 && p.X < w + 20 && p.Y > -20 && p.Y < h + 20);
        string F(double v) => v.ToString("0.#", Inv);
        string PathOf(IReadOnlyList<(double X, double Y)> pts, bool close) =>
            string.Join(" ", pts.Select((p, i) => $"{(i == 0 ? "M" : "L")}{F(p.X)},{F(p.Y)}")) + (close ? " Z" : "");

        var sb = new StringBuilder();
        sb.Append(Inv, $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" font-family="Helvetica, Arial, sans-serif">""");
        sb.Append(Inv, $"""<rect width="{w}" height="{h}" fill="{Paper}"/>""");

        // Roads under the parcels: the road reserve shows as the gaps between erven; main roads in
        // amber, as on street maps.
        var roads = m.Roads.Select(r => (Road: r, Pts: r.Line.Select(Px).ToList())).Where(r => OnMap(r.Pts)).ToList();
        foreach (var (road, pts) in roads.Where(r => IsMain(r.Road)))
            sb.Append(Inv, $"""<path d="{PathOf(pts, false)}" fill="none" stroke="{MainRoad}" stroke-width="{F(Math.Max((road.WidthM ?? 12) * scale, 4))}" stroke-linecap="round" stroke-linejoin="round"/>""");

        var sales = m.Sales.Where(s => s.Erf is not null).Select(s => s.Erf!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parcels = m.Parcels.Select(p => (P: p, Pts: p.Ring.Points.Select(Px).ToList())).Where(p => OnMap(p.Pts)).ToList();
        foreach (var (p, pts) in parcels)
        {
            var isSubject = p.Erf is not null && string.Equals(p.Erf, m.SubjectErf, StringComparison.OrdinalIgnoreCase);
            var isSale = !isSubject && p.Erf is not null && sales.Contains(p.Erf);
            var (fill, stroke, sw) = isSubject ? (SubjectFill, SubjectStroke, 1.6)
                : isSale ? (SaleFill, SaleStroke, 1.2)
                : (ParcelFill, ParcelStroke, 0.7);
            sb.Append(Inv, $"""<path d="{PathOf(pts, true)}" fill="{fill}" fill-opacity="{(isSubject || isSale ? "0.88" : "1")}" stroke="{stroke}" stroke-width="{F(sw)}" stroke-linejoin="round"/>""");
        }

        // Street numbers where the erf is big enough on the page to hold one.
        var numberSize = m.Block ? 12.0 : 6.5;
        foreach (var (p, pts) in parcels)
        {
            if (p.Number is null) continue;
            var (minX, maxX) = (pts.Min(q => q.X), pts.Max(q => q.X));
            var (minY, maxY) = (pts.Min(q => q.Y), pts.Max(q => q.Y));
            if (maxX - minX < numberSize * 1.6 || maxY - minY < numberSize * 1.3) continue;
            var c = Geo.Centroid(p.Ring);
            var (cx, cy) = Px(c);
            var ink = p.Erf is not null && (sales.Contains(p.Erf) || string.Equals(p.Erf, m.SubjectErf, StringComparison.OrdinalIgnoreCase))
                ? "#FFFFFF" : NumberInk;
            sb.Append(Inv, $"""<text x="{F(cx)}" y="{F(cy + numberSize * 0.35)}" font-size="{F(numberSize)}" fill="{ink}" text-anchor="middle">{Esc(p.Number)}</text>""");
        }

        // Road names, once per road, along its longest straight run on the map.
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nameSize = m.Block ? 13.0 : 9.0;
        foreach (var group in roads.Where(r => r.Road.Name.Length > 0)
                     .GroupBy(r => r.Road.Name, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Max(r => IsMain(r.Road) ? 1 : 0)))
        {
            var label = Title(group.Key) + Suffix(group.First().Road.Type);
            var best = group.SelectMany(r => r.Pts.Zip(r.Pts.Skip(1)))
                .Where(seg => Inside(seg.First, w, h, 30) && Inside(seg.Second, w, h, 30))
                .Select(seg => (seg.First, seg.Second, Len: Math.Sqrt(Math.Pow(seg.Second.X - seg.First.X, 2) + Math.Pow(seg.Second.Y - seg.First.Y, 2))))
                .OrderByDescending(seg => seg.Len)
                .FirstOrDefault();
            if (best.Len < label.Length * nameSize * 0.55 || !named.Add(group.Key)) continue;
            var (mx, my) = ((best.First.X + best.Second.X) / 2, (best.First.Y + best.Second.Y) / 2);
            var angle = Math.Atan2(best.Second.Y - best.First.Y, best.Second.X - best.First.X) * 180 / Math.PI;
            if (angle > 90) angle -= 180;
            if (angle < -90) angle += 180;
            sb.Append(Inv, $"""<text x="{F(mx)}" y="{F(my)}" dy="{F(nameSize * 0.35)}" font-size="{F(nameSize)}" font-style="italic" fill="{RoadInk}" text-anchor="middle" transform="rotate({F(angle)} {F(mx)} {F(my)})">{Esc(label)}</text>""");
        }

        // The radius the sales were drawn from.
        if (m.RadiusM is { } radius)
            sb.Append(Inv, $"""<circle cx="{F(w / 2)}" cy="{F(h / 2)}" r="{F(radius * scale)}" fill="{SaleFill}" fill-opacity="0.05" stroke="{SaleStroke}" stroke-width="2" stroke-dasharray="8 5"/>""");

        // Numbered pins on the sales, then the property's pin on top.
        foreach (var s in m.Sales)
        {
            var (x, y) = Px(s.Location);
            if (!Inside((x, y), w, h, 0)) continue;
            sb.Append(Pin(x, y, SaleStroke, s.Number.ToString(Inv), m.Block ? 1.3 : 1));
        }
        var (sx, sy) = m.Subject is null ? (w / 2, h / 2) : Px(Geo.Centroid(m.Subject));
        sb.Append(HousePin(sx, sy, SubjectStroke, m.Block ? 1.3 : 1.1));

        // Legend, scale bar, north and source.
        var legend = new List<(string Kind, string Text)> { ("subject", "This property") };
        if (m.Sales.Count > 0) legend.Add(("sale", "Comparable sale (numbered as in the table)"));
        if (m.RadiusM is { } r2) legend.Add(("radius", string.Create(Inv, $"Sales within {r2:0} m")));
        double lx = 14, ly = h - 16 - legend.Count * 19 - 10, lw = 250;
        sb.Append(Inv, $"""<rect x="{lx}" y="{F(ly)}" width="{lw}" height="{legend.Count * 19 + 14}" rx="6" fill="#FFFFFF" fill-opacity="0.94" stroke="#D5DCE4"/>""");
        for (int i = 0; i < legend.Count; i++)
        {
            double iy = ly + 16 + i * 19;
            var (kind, text) = legend[i];
            sb.Append(kind switch
            {
                "subject" => string.Create(Inv, $"""<rect x="{lx + 10}" y="{F(iy - 8)}" width="14" height="11" fill="{SubjectFill}" stroke="{SubjectStroke}"/>"""),
                "sale" => string.Create(Inv, $"""<rect x="{lx + 10}" y="{F(iy - 8)}" width="14" height="11" fill="{SaleFill}" stroke="{SaleStroke}"/>"""),
                _ => string.Create(Inv, $"""<circle cx="{lx + 17}" cy="{F(iy - 2.5)}" r="6" fill="none" stroke="{SaleStroke}" stroke-width="1.6" stroke-dasharray="3 2"/>"""),
            });
            sb.Append(Inv, $"""<text x="{lx + 32}" y="{F(iy + 1)}" font-size="10.5" fill="#2B3440">{Esc(text)}</text>""");
        }

        double target = 10;
        while (target * scale < 70) target = target switch { 10 => 20, 20 => 50, 50 => 100, 100 => 200, _ => target * 2 };
        double bar = target * scale, bx = w - 18 - bar, by = h - 22;
        sb.Append(Inv, $"""<rect x="{F(bx - 8)}" y="{F(by - 16)}" width="{F(bar + 16)}" height="30" rx="5" fill="#FFFFFF" fill-opacity="0.94"/>""");
        sb.Append(Inv, $"""<g stroke="#2B3440" stroke-width="1.6"><line x1="{F(bx)}" y1="{F(by)}" x2="{F(bx + bar)}" y2="{F(by)}"/><line x1="{F(bx)}" y1="{F(by - 4)}" x2="{F(bx)}" y2="{F(by + 4)}"/><line x1="{F(bx + bar)}" y1="{F(by - 4)}" x2="{F(bx + bar)}" y2="{F(by + 4)}"/></g>""");
        sb.Append(Inv, $"""<text x="{F(bx + bar / 2)}" y="{F(by - 5)}" font-size="10" fill="#2B3440" text-anchor="middle">{target:0} m</text>""");

        double nx = w - 30, ny = 34;
        sb.Append(Inv, $"""<circle cx="{nx}" cy="{ny}" r="17" fill="#FFFFFF" fill-opacity="0.94" stroke="#D5DCE4"/><polygon points="{nx},{ny - 12} {nx - 6},{ny + 4} {nx},{ny} {nx + 6},{ny + 4}" fill="#2B3440"/><text x="{nx}" y="{ny + 14}" font-size="8" fill="#2B3440" text-anchor="middle" font-weight="bold">N</text>""");

        sb.Append(Inv, $"""<text x="{F(w - 12)}" y="{F(h - 44)}" font-size="8" fill="#6B7682" text-anchor="end">{Esc(m.Source)}</text>""");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static bool IsMain(MapRoad r) =>
        (r.WidthM ?? 0) >= 13 || (r.Type ?? "").Contains("Main", StringComparison.OrdinalIgnoreCase)
        || (r.Type ?? "").Contains("Boulevard", StringComparison.OrdinalIgnoreCase)
        || (r.Type ?? "").Contains("Highway", StringComparison.OrdinalIgnoreCase);

    private static bool Inside((double X, double Y) p, double w, double h, double margin) =>
        p.X > margin && p.X < w - margin && p.Y > margin && p.Y < h - margin;

    /// <summary>A round pin with a number, its point on the erf.</summary>
    private static string Pin(double x, double y, string colour, string text, double k) =>
        string.Create(CultureInfo.InvariantCulture,
            $"""<g transform="translate({x:0.#} {y:0.#}) scale({k:0.##})"><path d="M0,0 L-5,-9 A10,10 0 1 1 5,-9 Z" fill="{colour}" stroke="#FFFFFF" stroke-width="1.5"/><text x="0" y="-13" font-size="10" font-weight="bold" fill="#FFFFFF" text-anchor="middle">{text}</text></g>""");

    /// <summary>The property's pin: a house in a red drop.</summary>
    private static string HousePin(double x, double y, string colour, double k) =>
        string.Create(CultureInfo.InvariantCulture,
            $"""<g transform="translate({x:0.#} {y:0.#}) scale({k:0.##})"><path d="M0,0 L-6,-11 A12,12 0 1 1 6,-11 Z" fill="{colour}" stroke="#FFFFFF" stroke-width="1.6"/><path d="M0,-27 L-7,-20 L-5,-20 L-5,-13 L5,-13 L5,-20 L7,-20 Z" fill="#FFFFFF"/></g>""");

    private static string Title(string s) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    /// <summary>"Street" → " Str", as street maps abbreviate.</summary>
    private static string Suffix(string? type) => (type ?? "").ToLowerInvariant() switch
    {
        "street" => " Str",
        "road" => " Rd",
        "avenue" => " Ave",
        "drive" => " Dr",
        "crescent" => " Cres",
        "close" => " Cl",
        "lane" => " Ln",
        "way" => " Way",
        "boulevard" => " Blvd",
        "place" => " Pl",
        "circle" => " Cir",
        _ => "",
    };

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
