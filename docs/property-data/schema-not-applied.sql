/* =============================================================================================
   Property data cache + report store — SQL Server (2019+ / Azure SQL)
   Idempotent: safe to run repeatedly.

   Design notes
   ------------
   * This is a CACHE of public municipal data plus OUR OWN derived output. One report should be one
     fetch: the app reads from here and only calls the City when a row is missing or stale.
   * dbo.Property is keyed on (Municipality, Erf, Suburb) because erf numbers repeat across
     townships. ValuationRef is unique where present.
   * Owner data is deliberately NOT stored alongside the property. It lives in dbo.OwnerSnapshot
     with a retention date, and every read is written to dbo.OwnerAccessLog. See §POPIA at the end.
   * Geometry is stored twice: BoundaryGeoJson for the API/SVG path, and Boundary geography for
     spatial queries (nearest comparables, radius search) once you want them.
   ============================================================================================= */

IF SCHEMA_ID('prop') IS NULL EXEC('CREATE SCHEMA prop');
GO

/* ---------------------------------------------------------------------------------------------
   Property
   --------------------------------------------------------------------------------------------- */
IF OBJECT_ID('prop.Property') IS NULL
BEGIN
    CREATE TABLE prop.Property
    (
        PropertyId          BIGINT IDENTITY(1,1) PRIMARY KEY,
        Municipality        VARCHAR(20)   NOT NULL DEFAULT 'coct',
        Erf                 VARCHAR(30)   NOT NULL,
        Sg26Code            VARCHAR(40)   NULL,
        ValuationRef        VARCHAR(30)   NULL,
        Township            NVARCHAR(80)  NOT NULL,
        Suburb              NVARCHAR(80)  NOT NULL,
        FormattedAddress    NVARCHAR(200) NOT NULL,
        StreetNo            INT           NULL,
        StreetName          NVARCHAR(80)  NULL,
        StreetType          NVARCHAR(30)  NULL,

        Latitude            DECIMAL(9,6)  NULL,
        Longitude           DECIMAL(9,6)  NULL,

        ExtentM2Deed        DECIMAL(12,2) NULL,   -- from the roll; preferred
        ExtentM2Geodesic    DECIMAL(12,2) NULL,   -- computed from the ring; never Shape__Area
        DwellingExtentM2    DECIMAL(12,2) NULL,   -- the City's own building m²

        ZoningCode          NVARCHAR(20)  NULL,
        ZoningDescription   NVARCHAR(200) NULL,
        Ward                NVARCHAR(20)  NULL,
        SubCouncil          NVARCHAR(20)  NULL,
        LegalStatus         NVARCHAR(40)  NULL,

        BoundaryGeoJson     NVARCHAR(MAX) NULL,
        Boundary            GEOGRAPHY     NULL,

        SourceFetchedAt     DATETIME2(0)  NOT NULL,
        RawJson             NVARCHAR(MAX) NULL,   -- keep the provider payload for replay/debugging
        CreatedAt           DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt           DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_Property_Muni_Erf_Suburb ON prop.Property (Municipality, Erf, Suburb);
    CREATE UNIQUE INDEX UX_Property_ValuationRef    ON prop.Property (ValuationRef) WHERE ValuationRef IS NOT NULL;
    CREATE INDEX IX_Property_Sg26                   ON prop.Property (Sg26Code) WHERE Sg26Code IS NOT NULL;
    CREATE INDEX IX_Property_Address                ON prop.Property (Municipality, StreetName, StreetNo) INCLUDE (Suburb, Erf);
    CREATE INDEX IX_Property_Stale                  ON prop.Property (SourceFetchedAt);
END
GO

/* ---------------------------------------------------------------------------------------------
   Municipal valuation (one row per roll version per property — keep the history)
   --------------------------------------------------------------------------------------------- */
IF OBJECT_ID('prop.MunicipalValuation') IS NULL
BEGIN
    CREATE TABLE prop.MunicipalValuation
    (
        ValuationId           BIGINT IDENTITY(1,1) PRIMARY KEY,
        PropertyId            BIGINT        NOT NULL REFERENCES prop.Property(PropertyId),
        RollVersion           VARCHAR(20)   NOT NULL,     -- 'GV2025', 'SV03/GV2022'
        ValueZar              DECIMAL(18,2) NOT NULL,
        ValueAsAt             DATE          NOT NULL,     -- GV2025 = 2025-07-01
        RatingCategory        NVARCHAR(40)  NULL,
        RegisteredDescription NVARCHAR(120) NULL,
        ExtentM2              DECIMAL(12,2) NULL,
        EffectiveFrom         DATE          NULL,
        DisputeExpiry         DATE          NULL,
        FetchedAt             DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_Valuation_Property_Roll ON prop.MunicipalValuation (PropertyId, RollVersion);
END
GO

/* ---------------------------------------------------------------------------------------------
   Buildings and approved work
   --------------------------------------------------------------------------------------------- */
IF OBJECT_ID('prop.Building') IS NULL
BEGIN
    CREATE TABLE prop.Building
    (
        BuildingId      BIGINT IDENTITY(1,1) PRIMARY KEY,
        PropertyId      BIGINT        NOT NULL REFERENCES prop.Property(PropertyId),
        RoofM2          DECIMAL(10,1) NOT NULL,
        HeightM         DECIMAL(6,2)  NULL,
        CapturedYyyyMm  INT           NULL,          -- 201312 => the footprint is from Dec 2013
        AcquisitionMethod NVARCHAR(40) NULL,
        OutlineGeoJson  NVARCHAR(MAX) NULL,
        FetchedAt       DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Building_Property ON prop.Building (PropertyId);
END
GO

IF OBJECT_ID('prop.ApprovedWork') IS NULL
BEGIN
    CREATE TABLE prop.ApprovedWork
    (
        ApprovedWorkId   BIGINT IDENTITY(1,1) PRIMARY KEY,
        PropertyId       BIGINT         NOT NULL REFERENCES prop.Property(PropertyId),
        CaseId           BIGINT         NULL,
        SubmissionDate   DATE           NULL,
        ApprovalDate     DATE           NULL,
        CompletionDate   DATE           NULL,
        OccupancyDate    DATE           NULL,
        Description      NVARCHAR(300)  NULL,
        PrimaryCategory  NVARCHAR(100)  NULL,
        SecondaryCategory NVARCHAR(100) NULL,
        AreaM2           DECIMAL(10,1)  NULL,
        ValueZar         DECIMAL(18,2)  NULL,
        Units            INT            NULL,
        FetchedAt        DATETIME2(0)   NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_ApprovedWork_Property ON prop.ApprovedWork (PropertyId, ApprovalDate DESC);
    CREATE UNIQUE INDEX UX_ApprovedWork_Case ON prop.ApprovedWork (CaseId) WHERE CaseId IS NOT NULL;
END
GO

/* ---------------------------------------------------------------------------------------------
   Suburb benchmark (GV2022 -> GV2025 medians, 659 Cape Town suburbs)
   --------------------------------------------------------------------------------------------- */
IF OBJECT_ID('prop.SuburbBenchmark') IS NULL
BEGIN
    CREATE TABLE prop.SuburbBenchmark
    (
        SuburbBenchmarkId BIGINT IDENTITY(1,1) PRIMARY KEY,
        Municipality      VARCHAR(20)   NOT NULL DEFAULT 'coct',
        Suburb            NVARCHAR(80)  NOT NULL,
        ResidentialCount  INT           NULL,
        MedianLandM2      DECIMAL(10,1) NULL,
        MedianBuildingM2  DECIMAL(10,1) NULL,
        Gv2022Zar         DECIMAL(18,2) NULL,
        Gv2025Zar         DECIMAL(18,2) NULL,
        AnnualGrowthPct   AS CASE WHEN Gv2022Zar > 0
                                  THEN (POWER(Gv2025Zar / Gv2022Zar, 1.0/3.0) - 1) * 100
                             END PERSISTED,
        FetchedAt         DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_SuburbBenchmark ON prop.SuburbBenchmark (Municipality, Suburb);
END
GO

/* ---------------------------------------------------------------------------------------------
   Comparable sales.
   Stored as observed (public deeds/roll data), with our filter verdict alongside so a report can
   be reproduced exactly as it was issued.
   --------------------------------------------------------------------------------------------- */
IF OBJECT_ID('prop.SaleObservation') IS NULL
BEGIN
    CREATE TABLE prop.SaleObservation
    (
        SaleObservationId BIGINT IDENTITY(1,1) PRIMARY KEY,
        Municipality      VARCHAR(20)   NOT NULL DEFAULT 'coct',
        ValuationRef      VARCHAR(30)   NOT NULL,
        Address           NVARCHAR(200) NOT NULL,
        RegisteredDescription NVARCHAR(120) NULL,
        Erf               VARCHAR(30)   NULL,
        Suburb            NVARCHAR(80)  NULL,
        ErfExtentM2       DECIMAL(12,2) NULL,
        DwellingExtentM2  DECIMAL(12,2) NULL,
        SaleDate          DATE          NOT NULL,
        SalePriceZar      DECIMAL(18,2) NOT NULL,
        PricePerDwellingM2 AS (CASE WHEN DwellingExtentM2 > 0 THEN SalePriceZar / DwellingExtentM2 END) PERSISTED,
        SourceUrl         NVARCHAR(400) NULL,
        FetchedAt         DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_Sale_Ref_Date_Price ON prop.SaleObservation (ValuationRef, SaleDate, SalePriceZar);
    CREATE INDEX IX_Sale_Suburb_Date ON prop.SaleObservation (Municipality, Suburb, SaleDate DESC)
        INCLUDE (SalePriceZar, DwellingExtentM2, ErfExtentM2);
END
GO

/* ---------------------------------------------------------------------------------------------
   Reports issued. Immutable once issued — a valuation you handed someone must stay reproducible.
   --------------------------------------------------------------------------------------------- */
IF OBJECT_ID('prop.Report') IS NULL
BEGIN
    CREATE TABLE prop.Report
    (
        ReportId          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        PropertyId        BIGINT        NOT NULL REFERENCES prop.Property(PropertyId),
        IssuedByUserId    NVARCHAR(100) NOT NULL,
        IssuedAt          DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
        ReportDate        DATE          NOT NULL,

        IndicativeLowZar  DECIMAL(18,2) NULL,
        IndicativeMidZar  DECIMAL(18,2) NULL,
        IndicativeHighZar DECIMAL(18,2) NULL,
        MedianPricePerDwellingM2 DECIMAL(18,2) NULL,
        ComparablesIncluded INT         NULL,
        ComparablesExcluded INT         NULL,

        IncludesOwnerData BIT           NOT NULL DEFAULT 0,
        RecipientIsOwner  BIT           NOT NULL DEFAULT 0,   -- drives what the PDF may contain
        PrintedCopies     INT           NULL,

        SnapshotJson      NVARCHAR(MAX) NOT NULL,             -- the whole PropertyReportDto as issued
        SitePlanSvg       NVARCHAR(MAX) NULL,                 -- ours, so safe to keep
        PdfBlobUri        NVARCHAR(400) NULL
    );
    CREATE INDEX IX_Report_Property ON prop.Report (PropertyId, IssuedAt DESC);
    CREATE INDEX IX_Report_User     ON prop.Report (IssuedByUserId, IssuedAt DESC);
END
GO

/* ---------------------------------------------------------------------------------------------
   Provenance — one row per field per fetch, so every number in the report can cite its source.
   --------------------------------------------------------------------------------------------- */
IF OBJECT_ID('prop.Provenance') IS NULL
BEGIN
    CREATE TABLE prop.Provenance
    (
        ProvenanceId BIGINT IDENTITY(1,1) PRIMARY KEY,
        PropertyId   BIGINT        NOT NULL REFERENCES prop.Property(PropertyId),
        FieldName    NVARCHAR(60)  NOT NULL,
        SourceUrl    NVARCHAR(400) NOT NULL,
        FetchedAt    DATETIME2(0)  NOT NULL
    );
    CREATE INDEX IX_Provenance_Property ON prop.Provenance (PropertyId, FieldName);
END
GO

/* =============================================================================================
   POPIA
   -----
   Owner data is personal information. Two rules encoded here:
     1. It is stored separately, with an explicit RetentionUntil, so a cleanup job can delete it
        without touching the property cache.
     2. Every read is logged against a user and a stated purpose. If you cannot say why a lookup
        happened, you cannot defend it.
   Do not join OwnerSnapshot into the comparables view: third parties' names have no place in
   someone else's valuation report.
   ============================================================================================= */
IF OBJECT_ID('prop.OwnerSnapshot') IS NULL
BEGIN
    CREATE TABLE prop.OwnerSnapshot
    (
        OwnerSnapshotId BIGINT IDENTITY(1,1) PRIMARY KEY,
        PropertyId      BIGINT        NOT NULL REFERENCES prop.Property(PropertyId),
        Provider        VARCHAR(40)   NOT NULL,          -- 'afrigis'
        OwnerNames      NVARCHAR(400) NULL,
        TitleDeed       VARCHAR(40)   NULL,
        PurchasePriceZar DECIMAL(18,2) NULL,
        PurchaseDate    DATE          NULL,
        RegistrationDate DATE         NULL,
        PayloadJson     NVARCHAR(MAX) NULL,
        FetchedAt       DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
        RetentionUntil  DATE          NOT NULL           -- set it on insert; no indefinite retention
    );
    CREATE INDEX IX_OwnerSnapshot_Property  ON prop.OwnerSnapshot (PropertyId, FetchedAt DESC);
    CREATE INDEX IX_OwnerSnapshot_Retention ON prop.OwnerSnapshot (RetentionUntil);
END
GO

IF OBJECT_ID('prop.OwnerAccessLog') IS NULL
BEGIN
    CREATE TABLE prop.OwnerAccessLog
    (
        OwnerAccessLogId BIGINT IDENTITY(1,1) PRIMARY KEY,
        UserId           NVARCHAR(100) NOT NULL,
        Municipality     VARCHAR(20)   NOT NULL,
        Erf              VARCHAR(30)   NOT NULL,
        Purpose          NVARCHAR(200) NOT NULL,
        AccessedAt       DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
        IpAddress        VARCHAR(45)   NULL
    );
    CREATE INDEX IX_OwnerAccessLog_User ON prop.OwnerAccessLog (UserId, AccessedAt DESC);
    CREATE INDEX IX_OwnerAccessLog_Erf  ON prop.OwnerAccessLog (Municipality, Erf, AccessedAt DESC);
END
GO

/* ---------------------------------------------------------------------------------------------
   Upserts
   --------------------------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE prop.UpsertProperty
    @Municipality VARCHAR(20), @Erf VARCHAR(30), @Suburb NVARCHAR(80),
    @Sg26Code VARCHAR(40) = NULL, @ValuationRef VARCHAR(30) = NULL,
    @Township NVARCHAR(80), @FormattedAddress NVARCHAR(200),
    @StreetNo INT = NULL, @StreetName NVARCHAR(80) = NULL, @StreetType NVARCHAR(30) = NULL,
    @Latitude DECIMAL(9,6) = NULL, @Longitude DECIMAL(9,6) = NULL,
    @ExtentM2Deed DECIMAL(12,2) = NULL, @ExtentM2Geodesic DECIMAL(12,2) = NULL,
    @DwellingExtentM2 DECIMAL(12,2) = NULL,
    @ZoningCode NVARCHAR(20) = NULL, @ZoningDescription NVARCHAR(200) = NULL,
    @Ward NVARCHAR(20) = NULL, @SubCouncil NVARCHAR(20) = NULL, @LegalStatus NVARCHAR(40) = NULL,
    @BoundaryGeoJson NVARCHAR(MAX) = NULL, @RawJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    MERGE prop.Property AS t
    USING (SELECT @Municipality AS Municipality, @Erf AS Erf, @Suburb AS Suburb) AS s
        ON t.Municipality = s.Municipality AND t.Erf = s.Erf AND t.Suburb = s.Suburb
    WHEN MATCHED THEN UPDATE SET
        Sg26Code = COALESCE(@Sg26Code, t.Sg26Code),
        ValuationRef = COALESCE(@ValuationRef, t.ValuationRef),
        Township = @Township,
        FormattedAddress = @FormattedAddress,
        StreetNo = COALESCE(@StreetNo, t.StreetNo),
        StreetName = COALESCE(@StreetName, t.StreetName),
        StreetType = COALESCE(@StreetType, t.StreetType),
        Latitude = COALESCE(@Latitude, t.Latitude),
        Longitude = COALESCE(@Longitude, t.Longitude),
        ExtentM2Deed = COALESCE(@ExtentM2Deed, t.ExtentM2Deed),
        ExtentM2Geodesic = COALESCE(@ExtentM2Geodesic, t.ExtentM2Geodesic),
        DwellingExtentM2 = COALESCE(@DwellingExtentM2, t.DwellingExtentM2),
        ZoningCode = COALESCE(@ZoningCode, t.ZoningCode),
        ZoningDescription = COALESCE(@ZoningDescription, t.ZoningDescription),
        Ward = COALESCE(@Ward, t.Ward),
        SubCouncil = COALESCE(@SubCouncil, t.SubCouncil),
        LegalStatus = COALESCE(@LegalStatus, t.LegalStatus),
        BoundaryGeoJson = COALESCE(@BoundaryGeoJson, t.BoundaryGeoJson),
        RawJson = COALESCE(@RawJson, t.RawJson),
        SourceFetchedAt = SYSUTCDATETIME(),
        UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT
        (Municipality, Erf, Suburb, Sg26Code, ValuationRef, Township, FormattedAddress,
         StreetNo, StreetName, StreetType, Latitude, Longitude,
         ExtentM2Deed, ExtentM2Geodesic, DwellingExtentM2,
         ZoningCode, ZoningDescription, Ward, SubCouncil, LegalStatus,
         BoundaryGeoJson, RawJson, SourceFetchedAt)
        VALUES
        (@Municipality, @Erf, @Suburb, @Sg26Code, @ValuationRef, @Township, @FormattedAddress,
         @StreetNo, @StreetName, @StreetType, @Latitude, @Longitude,
         @ExtentM2Deed, @ExtentM2Geodesic, @DwellingExtentM2,
         @ZoningCode, @ZoningDescription, @Ward, @SubCouncil, @LegalStatus,
         @BoundaryGeoJson, @RawJson, SYSUTCDATETIME());

    SELECT PropertyId FROM prop.Property
    WHERE Municipality = @Municipality AND Erf = @Erf AND Suburb = @Suburb;
END
GO

/* Bulk insert of area sales; ignores duplicates via the unique index. Pass a JSON array. */
CREATE OR ALTER PROCEDURE prop.UpsertSaleObservations
    @Municipality VARCHAR(20), @SourceUrl NVARCHAR(400), @SalesJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    WITH incoming AS (
        SELECT * FROM OPENJSON(@SalesJson)
        WITH (
            ValuationRef VARCHAR(30) '$.valuationRef',
            Address NVARCHAR(200) '$.address',
            RegisteredDescription NVARCHAR(120) '$.registeredDescription',
            Erf VARCHAR(30) '$.erf',
            Suburb NVARCHAR(80) '$.suburb',
            ErfExtentM2 DECIMAL(12,2) '$.erfExtentM2',
            DwellingExtentM2 DECIMAL(12,2) '$.dwellingExtentM2',
            SaleDate DATE '$.saleDate',
            SalePriceZar DECIMAL(18,2) '$.salePriceZar'
        )
    )
    MERGE prop.SaleObservation AS t
    USING incoming AS s
        ON t.ValuationRef = s.ValuationRef AND t.SaleDate = s.SaleDate AND t.SalePriceZar = s.SalePriceZar
    WHEN NOT MATCHED THEN INSERT
        (Municipality, ValuationRef, Address, RegisteredDescription, Erf, Suburb,
         ErfExtentM2, DwellingExtentM2, SaleDate, SalePriceZar, SourceUrl)
        VALUES
        (@Municipality, s.ValuationRef, s.Address, s.RegisteredDescription, s.Erf, s.Suburb,
         s.ErfExtentM2, s.DwellingExtentM2, s.SaleDate, s.SalePriceZar, @SourceUrl);
END
GO

/* Nightly: drop owner data past its retention date. Schedule it, don't trust yourself to remember. */
CREATE OR ALTER PROCEDURE prop.PurgeExpiredOwnerData
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM prop.OwnerSnapshot WHERE RetentionUntil < CAST(SYSUTCDATETIME() AS DATE);
    SELECT @@ROWCOUNT AS PurgedRows;
END
GO

/* ---------------------------------------------------------------------------------------------
   Convenience view: a suburb's sold-price-per-m² by year, from cached observations.
   Useful for a chart in the report without another round trip to the City.
   --------------------------------------------------------------------------------------------- */
CREATE OR ALTER VIEW prop.vSuburbPricePerM2ByYear
AS
SELECT
    Municipality,
    Suburb,
    YEAR(SaleDate) AS SaleYear,
    COUNT(*)       AS Sales,
    CAST(AVG(PricePerDwellingM2) AS DECIMAL(18,2)) AS AvgPricePerDwellingM2,
    CAST(MIN(PricePerDwellingM2) AS DECIMAL(18,2)) AS MinPricePerDwellingM2,
    CAST(MAX(PricePerDwellingM2) AS DECIMAL(18,2)) AS MaxPricePerDwellingM2
FROM prop.SaleObservation
WHERE SalePriceZar > 100000 AND DwellingExtentM2 > 0      -- same hygiene as the analyzer
GROUP BY Municipality, Suburb, YEAR(SaleDate);
GO
