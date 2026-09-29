-- Load-shedding history (past only), filled from three CSV files by tools/ImportLoadShedding.
-- The CSVs are the contract; where their rows come from is a separate matter (see the tool's
-- README). The report leaves the section out while these tables are empty.
--
--   LoadSheddingStagePeriods   one row per announced stage period (SAST), with the exclusion tag
--                              ('coct' = did not apply to Cape Town, which ran its own stage)
--   LoadSheddingAreaSlots      each area's recurring monthly pattern: day of month, stage, times
--   LoadSheddingSuburbAreas    suburb -> area/block, per municipality
--
-- Not applied automatically -- run it once in SSMS or sqlcmd. Safe to re-run.

IF OBJECT_ID('dbo.LoadSheddingStagePeriods', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LoadSheddingStagePeriods] (
        [Id]         BIGINT IDENTITY (1, 1) NOT NULL,
        [StartLocal] DATETIME2 (0)  NOT NULL,
        [FinshLocal] DATETIME2 (0)  NOT NULL,
        [Stage]      TINYINT        NOT NULL,
        [ExcludeTag] VARCHAR (40)   NULL,
        [Source]     NVARCHAR (400) NULL,
        CONSTRAINT [PK_LoadSheddingStagePeriods] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    CREATE INDEX [IX_LoadSheddingStagePeriods_Range]
        ON [dbo].[LoadSheddingStagePeriods] ([StartLocal], [FinshLocal]);
END;

IF OBJECT_ID('dbo.LoadSheddingAreaSlots', 'U') IS NULL
    CREATE TABLE [dbo].[LoadSheddingAreaSlots] (
        [Area]        VARCHAR (80) NOT NULL,
        [DateOfMonth] TINYINT      NOT NULL,
        [Stage]       TINYINT      NOT NULL,
        [StartTime]   TIME (0)     NOT NULL,
        [FinshTime]   TIME (0)     NOT NULL,
        CONSTRAINT [PK_LoadSheddingAreaSlots] PRIMARY KEY CLUSTERED ([Area], [DateOfMonth], [Stage], [StartTime])
    );

IF OBJECT_ID('dbo.LoadSheddingSuburbAreas', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LoadSheddingSuburbAreas] (
        [Province]     VARCHAR (40)   NULL,
        [Municipality] NVARCHAR (120) NULL,
        [Suburb]       NVARCHAR (160) NOT NULL,
        [Area]         VARCHAR (80)   NOT NULL,
        [Provider]     VARCHAR (40)   NULL,
        [Source]       NVARCHAR (400) NULL,
        CONSTRAINT [PK_LoadSheddingSuburbAreas] PRIMARY KEY CLUSTERED ([Suburb], [Area])
    );
    CREATE INDEX [IX_LoadSheddingSuburbAreas_Suburb] ON [dbo].[LoadSheddingSuburbAreas] ([Suburb]);
END;
