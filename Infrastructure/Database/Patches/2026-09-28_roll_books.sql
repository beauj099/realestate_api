-- Valuation rolls read from published PDF "roll books" (tools/ImportRollBooks), for municipalities
-- with no roll search: every erf's category, street address, extent and market value. The books
-- carry no owner names.
--
-- One RollBookImports row per imported book (keyed on the file's SHA-256, so re-importing an
-- unchanged book is a no-op); its rows in RollBookEntries. A newer import of the same book
-- (municipality, area, roll) becomes current and the older one stays for reference.
--
-- Not applied automatically -- run it once in SSMS or sqlcmd. Safe to re-run.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.RollBookImports', 'U') IS NULL
    CREATE TABLE [dbo].[RollBookImports] (
        [Id]              INT IDENTITY (1, 1) NOT NULL,
        [Municipality]    VARCHAR (20)    NOT NULL,   -- 'drakenstein'
        [Area]            NVARCHAR (80)   NOT NULL,   -- the book's "Geographical Area", upper case: 'PAARL'
        [RollVersion]     VARCHAR (20)    NOT NULL,   -- 'GV2024'
        [DateOfValuation] DATE            NOT NULL,   -- read from the book's cover
        [EffectiveFrom]   DATE            NULL,
        [SourceUrl]       NVARCHAR (400)  NOT NULL,
        [FileSha256]      CHAR (64)       NOT NULL,
        [Pages]           INT             NOT NULL,
        [RowsRead]        INT             NOT NULL,
        [RowsRejected]    INT             NOT NULL,
        [SanityJson]      NVARCHAR (MAX)  NULL,
        [IsCurrent]       BIT             CONSTRAINT [DF_RollBookImports_IsCurrent] DEFAULT ((1)) NOT NULL,
        [ImportedAt]      DATETIME        CONSTRAINT [DF_RollBookImports_ImportedAt] DEFAULT (GETUTCDATE()) NOT NULL,
        CONSTRAINT [PK_RollBookImports] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RollBookImports_File' AND object_id = OBJECT_ID('dbo.RollBookImports'))
    CREATE UNIQUE NONCLUSTERED INDEX [UX_RollBookImports_File] ON [dbo].[RollBookImports] ([FileSha256]);

IF OBJECT_ID('dbo.RollBookEntries', 'U') IS NULL
    CREATE TABLE [dbo].[RollBookEntries] (
        [Id]            BIGINT IDENTITY (1, 1) NOT NULL,
        [ImportId]      INT             NOT NULL,
        [Municipality]  VARCHAR (20)    NOT NULL,
        [Area]          NVARCHAR (80)   NOT NULL,
        [Erf]           INT             NOT NULL,
        [Portion]       INT             NOT NULL,
        [IsGroupHead]   BIT             NOT NULL,     -- "5*": carries the value of a consolidated group
        [GroupHeadErf]  INT             NULL,         -- a member: "See :- Paarl 5*"
        [ValuedUnder]   NVARCHAR (120)  NULL,         -- a sectional-title scheme / share block name
        [Category]      VARCHAR (20)    NULL,         -- 'RES', 'COM', 'VACR', …
        [Address]       NVARCHAR (200)  NULL,
        [ExtentM2]      DECIMAL (14, 1) NULL,
        [ValueZar]      DECIMAL (18, 0) NULL,
        [Particulars]   NVARCHAR (300)  NULL,
        [Page]          INT             NOT NULL,
        CONSTRAINT [PK_RollBookEntries] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_RollBookEntries_Imports] FOREIGN KEY ([ImportId]) REFERENCES [dbo].[RollBookImports] ([Id])
    );

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RollBookEntries_Lookup' AND object_id = OBJECT_ID('dbo.RollBookEntries'))
    CREATE NONCLUSTERED INDEX [IX_RollBookEntries_Lookup]
        ON [dbo].[RollBookEntries] ([Municipality] ASC, [Area] ASC, [Erf] ASC, [Portion] ASC)
        INCLUDE ([ImportId], [IsGroupHead], [GroupHeadErf], [ValuedUnder], [Category], [Address], [ExtentM2], [ValueZar], [Particulars]);

COMMIT TRANSACTION;
