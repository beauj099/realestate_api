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
public sealed record MapSale(int Number, string? Erf, LatLng Location, bool Reference = false);

/// <summary>Another recent sale nearby (not one of the comparables): its erf, price and year.</summary>
public sealed record MapOtherSale(string? Erf, LatLng Location, decimal PriceZar, int Year);

/// <summary>A nearby place (school, shop, clinic…) by its group key, drawn with that group's icon.</summary>
public sealed record MapPlace(string Group, string Name, LatLng Location);

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
    bool Block = false,
    IReadOnlyList<MapPlace>? Places = null,
    IReadOnlyList<MapOtherSale>? OtherSales = null);

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
    private const string OtherFill = "#B9CDF3";
    private const string OtherStroke = "#6F93D8";
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
        var referenceErfs = m.Sales.Where(s => s.Reference && s.Erf is not null).Select(s => s.Erf!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = (m.OtherSales ?? []).Where(o => o.Erf is not null && !sales.Contains(o.Erf))
            .GroupBy(o => o.Erf!, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(o => o.Year).First()).ToList();
        var otherErfs = others.Select(o => o.Erf!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parcels = m.Parcels.Select(p => (P: p, Pts: p.Ring.Points.Select(Px).ToList())).Where(p => OnMap(p.Pts)).ToList();
        foreach (var (p, pts) in parcels)
        {
            var isSubject = p.Erf is not null && string.Equals(p.Erf, m.SubjectErf, StringComparison.OrdinalIgnoreCase);
            var isSale = !isSubject && p.Erf is not null && sales.Contains(p.Erf);
            var isOther = !isSubject && !isSale && p.Erf is not null && otherErfs.Contains(p.Erf);
            var (fill, stroke, sw) = isSubject ? (SubjectFill, SubjectStroke, 1.6)
                : isSale && referenceErfs.Contains(p.Erf!) ? (OtherFill, OtherStroke, 1.2)
                : isSale ? (SaleFill, SaleStroke, 1.2)
                : isOther ? (OtherFill, OtherStroke, 1.0)
                : (ParcelFill, ParcelStroke, 0.7);
            sb.Append(Inv, $"""<path d="{PathOf(pts, true)}" fill="{fill}" fill-opacity="{(isSubject || isSale ? "0.88" : "1")}" stroke="{stroke}" stroke-width="{F(sw)}" stroke-linejoin="round"/>""");
        }

        // Street numbers where the erf is big enough on the page to hold one.
        var numberSize = m.Block ? 10 : 6.5;
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
        var nameSize = m.Block ? 13 : 9.0;
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

        // Other recent sales: a small price tag on the erf.
        foreach (var o in others)
        {
            var (x, y) = Px(o.Location);
            if (!Inside((x, y), w, h, 20)) continue;
            var tag = $"{Money(o.PriceZar)} · {o.Year}";
            var tw = tag.Length * 6.4 + 10;
            sb.Append(Inv, $"""<rect x="{F(x - tw / 2)}" y="{F(y - 9)}" width="{F(tw)}" height="18" rx="4" fill="#FFFFFF" fill-opacity="0.95" stroke="{OtherStroke}" stroke-width="1"/>""");
            sb.Append(Inv, $"""<text x="{F(x)}" y="{F(y + 4)}" font-size="11.5" font-weight="bold" fill="{SaleStroke}" text-anchor="middle">{Esc(tag)}</text>""");
        }

        // Numbered pins on the sales, then the property's pin on top.
        foreach (var s in m.Sales)
        {
            var (x, y) = Px(s.Location);
            if (!Inside((x, y), w, h, 0)) continue;
            sb.Append(Pin(x, y, s.Reference ? OtherStroke : SaleStroke, s.Number.ToString(Inv), m.Block ? 1.5 : 1));
        }
        // Nearby places with their group's icon and name.
        var shownGroups = new List<string>();
        foreach (var place in m.Places ?? [])
        {
            var (x, y) = Px(place.Location);
            if (!Inside((x, y), w, h, 16) || !PlaceStyle.TryGetValue(place.Group, out var style)) continue;
            if (!shownGroups.Contains(place.Group)) shownGroups.Add(place.Group);
            sb.Append(Inv, $"""<circle cx="{F(x)}" cy="{F(y)}" r="15" fill="{style.Colour}" stroke="#FFFFFF" stroke-width="2"/>""");
            sb.Append(Inv, $"""<g transform="translate({F(x - 9.5)} {F(y - 9.5)}) scale(0.79)" fill="none" stroke="#FFFFFF" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"><path d="{style.Icon}"/></g>""");
            var label = place.Name.Length > 28 ? place.Name[..27] + "…" : place.Name;
            var tagW = label.Length * 7.3 + 12;
            var tagX = x + 18 + tagW > w ? x - 18 - tagW : x + 18;
            sb.Append(Inv, $"""<rect x="{F(tagX)}" y="{F(y - 11)}" width="{F(tagW)}" height="22" rx="5" fill="#FFFFFF" fill-opacity="0.94" stroke="{style.Colour}" stroke-width="1"/>""");
            sb.Append(Inv, $"""<text x="{F(tagX + 6)}" y="{F(y + 4.5)}" font-size="13" fill="#2B3440">{Esc(label)}</text>""");
        }

        var (sx, sy) = m.Subject is null ? (w / 2, h / 2) : Px(Geo.Centroid(m.Subject));
        sb.Append(HousePin(sx, sy, SubjectStroke, m.Block ? 1.3 : 1.1));

        // Legend, scale bar, north and source.
        var legend = new List<(string Kind, string Text)> { ("subject", "This property") };
        if (m.Sales.Any(s => !s.Reference)) legend.Add(("sale", "Comparable sale (numbered as in the table)"));
        if (m.Sales.Any(s => s.Reference)) legend.Add(("other", "Listed for reference, not in the range"));
        if (others.Count > 0) legend.Add(("otherTag", "Other recent sale (price and year)"));
        if (m.RadiusM is { } r2) legend.Add(("radius", string.Create(Inv, $"Sales within {r2:0} m")));
        foreach (var g in shownGroups) legend.Add(("place:" + g, PlaceStyle[g].Title));
        // The block view is printed smaller, so its legend is larger.
        var k = m.Block ? 1.25 : 1.0;
        double row = 19 * k, lx = 14, ly = h - 16 - legend.Count * row - 10, lw = 250 * k;
        sb.Append(Inv, $"""<rect x="{lx}" y="{F(ly)}" width="{F(lw)}" height="{F(legend.Count * row + 14)}" rx="6" fill="#FFFFFF" fill-opacity="0.94" stroke="#D5DCE4"/>""");
        sb.Append(Inv, $"""<g transform="translate({F(lx)} {F(ly)}) scale({F(k)}) translate({F(-lx)} {F(-ly)})">""");
        for (int i = 0; i < legend.Count; i++)
        {
            double iy = ly + 16 + i * 19;
            var (kind, text) = legend[i];
            if (kind.StartsWith("place:", StringComparison.Ordinal))
            {
                var style = PlaceStyle[kind[6..]];
                sb.Append(Inv, $"""<circle cx="{lx + 17}" cy="{F(iy - 2.5)}" r="7" fill="{style.Colour}"/><g transform="translate({lx + 12.5} {F(iy - 7)}) scale(0.375)" fill="none" stroke="#FFFFFF" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="{style.Icon}"/></g>""");
                sb.Append(Inv, $"""<text x="{lx + 32}" y="{F(iy + 1)}" font-size="10.5" fill="#2B3440">{Esc(text)}</text>""");
                continue;
            }
            sb.Append(kind switch
            {
                "subject" => string.Create(Inv, $"""<rect x="{lx + 10}" y="{F(iy - 8)}" width="14" height="11" fill="{SubjectFill}" stroke="{SubjectStroke}"/>"""),
                "sale" => string.Create(Inv, $"""<rect x="{lx + 10}" y="{F(iy - 8)}" width="14" height="11" fill="{SaleFill}" stroke="{SaleStroke}"/>"""),
                "other" => string.Create(Inv, $"""<rect x="{lx + 10}" y="{F(iy - 8)}" width="14" height="11" fill="{OtherFill}" stroke="{OtherStroke}"/>"""),
                "otherTag" => string.Create(Inv, $"""<rect x="{lx + 8}" y="{F(iy - 9)}" width="18" height="12" rx="3" fill="#FFFFFF" stroke="{OtherStroke}"/><rect x="{lx + 12}" y="{F(iy - 5)}" width="10" height="4" fill="{SaleStroke}"/>"""),
                _ => string.Create(Inv, $"""<circle cx="{lx + 17}" cy="{F(iy - 2.5)}" r="6" fill="none" stroke="{SaleStroke}" stroke-width="1.6" stroke-dasharray="3 2"/>"""),
            });
            sb.Append(Inv, $"""<text x="{lx + 32}" y="{F(iy + 1)}" font-size="10.5" fill="#2B3440">{Esc(text)}</text>""");
        }
        sb.Append("</g>");

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

    private static readonly Dictionary<string, string> Icons = new()
    {
        ["schools"] = "M22 9l-10 -4l-10 4l10 4l10 -4v6 M6 10.6v5.4a6 3 0 0 0 12 0v-5.4",
        ["shopping"] = "M4 19a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M15 19a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M17 17h-11v-14h-2 M6 5l14 1l-1 7h-13",
        ["health"] = "M8 8v-2a2 2 0 0 1 2 -2h4a2 2 0 0 1 2 2v2 M4 10a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2l0 -8 M10 14h4 M12 12v4",
        ["parks"] = "M16 5l3 3l-2 1l4 4l-3 1l4 4h-9 M15 21l0 -3 M8 13l-2 -2 M8 12l2 -2 M8 21v-13 M5.824 16a3 3 0 0 1 -2.743 -3.69a3 3 0 0 1 .304 -4.833a3 3 0 0 1 4.615 -3.707a3 3 0 0 1 4.614 3.707a3 3 0 0 1 .305 4.833a3 3 0 0 1 -2.919 3.695h-4l-.176 -.005",
        ["beach"] = "M17.553 16.75a7.5 7.5 0 0 0 -10.606 0 M18 3.804a6 6 0 0 0 -8.196 2.196l10.392 6a6 6 0 0 0 -2.196 -8.196 M16.732 10c1.658 -2.87 2.225 -5.644 1.268 -6.196c-.957 -.552 -3.075 1.326 -4.732 4.196 M15 9l-3 5.196 M3 19.25a2.4 2.4 0 0 1 1 -.25a2.4 2.4 0 0 1 2 1a2.4 2.4 0 0 0 2 1a2.4 2.4 0 0 0 2 -1a2.4 2.4 0 0 1 2 -1a2.4 2.4 0 0 1 2 1a2.4 2.4 0 0 0 2 1a2.4 2.4 0 0 0 2 -1a2.4 2.4 0 0 1 2 -1a2.4 2.4 0 0 1 1 .25",
        ["transport"] = "M21 13c0 -3.87 -3.37 -7 -10 -7h-8 M3 15h16a2 2 0 0 0 2 -2 M3 6v5h17.5 M3 11v4 M8 11v-5 M13 11v-4.5 M3 19h18",
        ["fuel"] = "M14 11h1a2 2 0 0 1 2 2v3a1.5 1.5 0 0 0 3 0v-7l-3 -3 M4 20v-14a2 2 0 0 1 2 -2h6a2 2 0 0 1 2 2v14 M3 20l12 0 M18 7v1a1 1 0 0 0 1 1h1 M4 11l10 0",
        ["police"] = "M12 3a12 12 0 0 0 8.5 3a12 12 0 0 1 -8.5 15a12 12 0 0 1 -8.5 -15a12 12 0 0 0 8.5 -3",
    };

    /// <summary>
    /// Each group of nearby places: its colour, legend title and icon (Tabler Icons, MIT: the
    /// same line icons as the report pack).
    /// </summary>
    private static readonly Dictionary<string, (string Colour, string Title, string Icon)> PlaceStyle = new()
    {
        ["schools"] = ("#7B4FD6", "School", Icons["schools"]),
        ["shopping"] = ("#E08A1E", "Shops", Icons["shopping"]),
        ["health"] = ("#D93A4A", "Health care", Icons["health"]),
        ["parks"] = ("#2E9E5B", "Park", Icons["parks"]),
        ["beach"] = ("#1E9BD7", "Beach", Icons["beach"]),
        ["transport"] = ("#4A5563", "Transport", Icons["transport"]),
        ["fuel"] = ("#0E7C86", "Petrol station", Icons["fuel"]),
        ["police"] = ("#1F3A93", "Police", Icons["police"]),
    };


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

    /// <summary>"R 2.95m", "R 850k": short enough for a tag on an erf.</summary>
    private static string Money(decimal zar) => zar >= 1_000_000
        ? string.Create(CultureInfo.InvariantCulture, $"R {zar / 1_000_000m:0.##}m")
        : string.Create(CultureInfo.InvariantCulture, $"R {zar / 1000m:0}k");

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
