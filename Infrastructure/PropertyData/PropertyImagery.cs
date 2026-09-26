using System.Globalization;
using Microsoft.Extensions.Options;
using RealEstateApi.Application.DTOs;

namespace RealEstateApi.Infrastructure.PropertyData;

/// <summary>Google Static Maps / Street View settings ("Imagery" section). No key, no imagery.</summary>
public class ImageryOptions
{
    public const string SectionName = "Imagery";

    public string? GoogleMapsApiKey { get; set; }
    public int SatelliteZoom { get; set; } = 19;
    public string SatelliteSize { get; set; } = "640x400";
    public string StreetViewSize { get; set; } = "640x400";
}

/// <summary>
/// Builds imagery references and carries the licence rules with them.
///
///   Satellite (Maps Static) — may appear in a printed report (a business document, under 5 000
///                             copies) provided the Google attribution stays visible next to the
///                             image and the image is not cropped or altered.
///   Street View Static      — must NOT appear in print or in a PDF. Screen only. Hence
///                             AllowedInPrint = false, which the app's PDF export filters on.
///
/// Never store the image bytes: the proxy endpoint re-requests them at render time, which also
/// keeps the API key off the phone. Cache the location, not the pictures.
/// </summary>
public class ImageryLinkBuilder(IOptions<ImageryOptions> options)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.GoogleMapsApiKey);

    public IReadOnlyList<ImageryRefDto> For(string erf, string municipality, string suburb, string? sg26, double? lat, double? lng)
    {
        if (lat is null || lng is null || !IsConfigured) return [];

        var q = $"?erf={Uri.EscapeDataString(erf)}&municipality={Uri.EscapeDataString(municipality)}" +
                $"&suburb={Uri.EscapeDataString(suburb)}" +
                (sg26 is null ? "" : $"&sg26={Uri.EscapeDataString(sg26)}");
        return
        [
            new ImageryRefDto("satellite", $"/api/property/imagery/satellite{q}",
                "Imagery © Google, Maxar Technologies", AllowedInPrint: true),
            new ImageryRefDto("streetview", $"/api/property/imagery/streetview{q}",
                "© Google Street View", AllowedInPrint: false),
        ];
    }

    public string SatelliteUrl(double lat, double lng)
    {
        var o = options.Value;
        return "https://maps.googleapis.com/maps/api/staticmap"
             + $"?center={Coord(lat)},{Coord(lng)}&zoom={o.SatelliteZoom}&size={o.SatelliteSize}"
             + $"&scale=2&maptype=satellite&key={o.GoogleMapsApiKey}";
    }

    public string StreetViewUrl(double lat, double lng)
    {
        var o = options.Value;
        return "https://maps.googleapis.com/maps/api/streetview"
             + $"?size={o.StreetViewSize}&scale=2&location={Coord(lat)},{Coord(lng)}"
             + $"&fov=75&pitch=5&return_error_code=true&key={o.GoogleMapsApiKey}";
    }

    private static string Coord(double v) => v.ToString(CultureInfo.InvariantCulture);
}
