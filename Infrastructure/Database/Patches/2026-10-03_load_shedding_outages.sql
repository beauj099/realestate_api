-- Load-shedding as carried out, per area, and the areas' outlines, filled by
-- tools/ImportLoadShedding --coct from the City of Cape Town's open data (see the tool's README).
--
--   LoadSheddingOutages     one row per recorded outage: area, local start (SAST), minutes, stage
--   LoadSheddingAreaShapes  each area's outline (WGS84 rings as JSON) with its bounding box, so
--                           the API finds a property's area by its location
--
-- The API reads the outages before the schedule tables of 2026-09-29_load_shedding.sql (which it
-- still uses for areas with no outages). Apply this patch BEFORE deploying the API that reads
-- these tables. Not applied automatically -- run it once in SSMS or sqlcmd. Safe to re-run.

IF OBJECT_ID('dbo.LoadSheddingOutages', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LoadSheddingOutages] (
        [Id]         BIGINT IDENTITY (1, 1) NOT NULL,
        [Area]       VARCHAR (80)  NOT NULL,   -- e.g. city-of-cape-town-area-9
        [StartLocal] DATETIME2 (0) NOT NULL,   -- SAST, as the City records it
        [Minutes]    INT           NOT NULL,
        [Stage]      TINYINT       NULL,       -- the City's stage, 1 to 8
        [Source]     VARCHAR (40)  NOT NULL,   -- e.g. coct-open-data; the import replaces per source
        CONSTRAINT [PK_LoadSheddingOutages] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    CREATE INDEX [IX_LoadSheddingOutages_Area]
        ON [dbo].[LoadSheddingOutages] ([Area], [StartLocal]) INCLUDE ([Minutes], [Stage]);
    CREATE INDEX [IX_LoadSheddingOutages_Source] ON [dbo].[LoadSheddingOutages] ([Source]);
END;

IF OBJECT_ID('dbo.LoadSheddingAreaShapes', 'U') IS NULL
    CREATE TABLE [dbo].[LoadSheddingAreaShapes] (
        [Area]         VARCHAR (80)   NOT NULL,
        [Municipality] NVARCHAR (120) NULL,      -- slug, e.g. city-of-cape-town
        [MinLat]       FLOAT          NOT NULL,
        [MinLng]       FLOAT          NOT NULL,
        [MaxLat]       FLOAT          NOT NULL,
        [MaxLng]       FLOAT          NOT NULL,
        [RingsJson]    NVARCHAR (MAX) NOT NULL,  -- [[[lng,lat],...],...]; parts and holes, even-odd
        [Source]       VARCHAR (40)   NOT NULL,
        CONSTRAINT [PK_LoadSheddingAreaShapes] PRIMARY KEY CLUSTERED ([Area])
    );
