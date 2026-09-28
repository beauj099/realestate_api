namespace PropertyData.RollBooks
{
    /// <summary>
    /// Municipalities whose roll we read from published PDF books, and the books. Before adding a
    /// municipality, open one of its books: the footer must name PenSoft and the columns sit where
    /// <see cref="RollBookParser"/> expects (it refuses pages that don't), then run
    /// tools/ImportRollBooks without --apply and read the sanity report.
    /// </summary>
    public static class RollBookCatalogue
    {
        public sealed record RollBookMunicipality(
            string Municipality,     // our code, e.g. "drakenstein"
            string Name,             // "Drakenstein Municipality"
            string ParcelKeyPrefix,  // national cadastre key prefix = demarcation code, "W023"
            string RollVersion,      // "GV2024"
            DateOnly DateOfValuation, // must match what the books print; the importer refuses a book that differs
            DateOnly EffectiveFrom,  // the roll's first day in force
            string PeriodLabel,      // "2025–2029", for the report
            IReadOnlyList<string> BookUrls);

        /// <summary>The category key printed at the front of every PenSoft book.</summary>
        public static string DescribeCategory(string? code) => code?.Trim().ToUpperInvariant() switch
        {
            "RES" => "Residential",
            "VACR" => "Vacant land (residential)",
            "VACB" => "Vacant land (commercial)",
            "IND" => "Industrial",
            "COM" => "Business and commercial",
            "AGRI" => "Agricultural",
            "PSP" => "Public service purposes",
            "MUN" => "Municipal",
            "PSI" => "Public service infrastructure",
            "PBO" => "Public benefit organisation",
            "MULTI*" or "MULTI" => "Multiple purposes",
            "PROS" => "Private open space",
            "SS GARAGE" => "Sectional title garage",
            "RELIG" => "Place of worship",
            null or "" => "",
            var other => other,
        };

        private const string Drakenstein = "https://www.drakenstein.gov.za/sites/dw/DocumentLibrary";

        /// <summary>
        /// Drakenstein GV2024: valued as at 1 July 2024 (printed on each book), in force 1 July 2025
        /// to 30 June 2029 (the municipality's valuation-roll page). Town books only: the "RD"
        /// books are farms, which the cadastre names differently, and sectional-title units are in
        /// a separate book with a different layout. Supplementary rolls (SV1–SV3) are not read yet.
        /// </summary>
        public static readonly IReadOnlyList<RollBookMunicipality> All =
        [
            new("drakenstein", "Drakenstein Municipality", "W023", "GV2024",
                DateOfValuation: new DateOnly(2024, 7, 1), EffectiveFrom: new DateOnly(2025, 7, 1), PeriodLabel: "2025–2029", BookUrls:
            [
                $"{Drakenstein}/GV2024%20PublishedTown%20Paarl.pdf",
                $"{Drakenstein}/GV2024%20PublishedTown%20Wellington.pdf",
                $"{Drakenstein}/GV2024%20PublishedTown%20Mbekweni.pdf",
                $"{Drakenstein}/GV2024%20PublishedTown%20Gouda.pdf",
                $"{Drakenstein}/GV2024%20PublishedTown%20Saron.pdf",
                $"{Drakenstein}/GV2024%20PublishedTown%20Bainskloof%20Pass.pdf",
            ]),
        ];

        public static RollBookMunicipality? ForParcel(string? sg26) =>
            sg26 is null ? null : All.FirstOrDefault(m => sg26.StartsWith(m.ParcelKeyPrefix, StringComparison.OrdinalIgnoreCase));

        public static RollBookMunicipality? ForMunicipality(string municipality) =>
            All.FirstOrDefault(m => m.Municipality == municipality);
    }
}
