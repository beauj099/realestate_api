-- Agent-captured comparables: sales an agent knows about ("I handled this sale", "the selling
-- agent told me", "I heard it sold for about this"), shared with every agent working the same
-- suburb. The capturing agent is never shown on a report.
--
-- They are the comparables source wherever no free sales data exists (everywhere outside Cape
-- Town and Johannesburg), and a supplement everywhere else: an agent knows the price weeks
-- before it reaches the deeds office, and knows the condition, which no dataset carries.
--
-- EvidenceLevel weights the entry (Weight, computed): a signed offer counts more than hearsay.
-- A second agent logging the same sale corroborates it (CorroborationCount) instead of adding a
-- row. When a municipal sales record for the same address, date and price turns up, the entry
-- is promoted to Verified / DeedsVerified; a materially different price marks it Disputed
-- (weight 0, still listed).
--
-- Not applied automatically -- run it once in SSMS or sqlcmd. Safe to re-run.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.AgentComparables', 'U') IS NULL
    CREATE TABLE [dbo].[AgentComparables] (
        [Id]                 UNIQUEIDENTIFIER CONSTRAINT [DF_AgentComparables_Id] DEFAULT (NEWSEQUENTIALID()) NOT NULL,
        [CapturedByUserId]   INT              NOT NULL,
        [CapturedAt]         DATETIME         CONSTRAINT [DF_AgentComparables_CapturedAt] DEFAULT (GETUTCDATE()) NOT NULL,

        [Municipality]       VARCHAR (20)     NOT NULL,   -- 'coct', 'coj', 'national', …
        [Suburb]             NVARCHAR (80)    NOT NULL,   -- upper case, as the report's suburb
        [Address]            NVARCHAR (200)   NOT NULL,
        [Erf]                NVARCHAR (30)    NULL,
        [Latitude]           DECIMAL (9, 6)   NULL,
        [Longitude]          DECIMAL (9, 6)   NULL,

        [ErfM2]              DECIMAL (10, 1)  NULL,
        [FloorM2]            DECIMAL (10, 1)  NULL,
        [Bedrooms]           TINYINT          NULL,
        [Bathrooms]          TINYINT          NULL,
        [Garages]            TINYINT          NULL,
        [PropertyTypeId]     INT              NULL,
        [Condition]          VARCHAR (20)     NULL,       -- Poor | Fair | Good | VeryGood | Renovated

        [SaleDate]           DATE             NOT NULL,
        [SalePriceZar]       DECIMAL (18, 2)  NOT NULL,
        [EvidenceLevel]      VARCHAR (20)     NOT NULL,   -- Hearsay | ColleagueConfirmed | SignedOffer | OwnTransaction | DeedsVerified
        [Notes]              NVARCHAR (600)   NULL,
        [CorroborationCount] INT              CONSTRAINT [DF_AgentComparables_Corroboration] DEFAULT ((0)) NOT NULL,

        [Verification]       VARCHAR (12)     CONSTRAINT [DF_AgentComparables_Verification] DEFAULT ('Unverified') NOT NULL,
        [VerifiedAgainst]    NVARCHAR (200)   NULL,
        [VerifiedAt]         DATETIME         NULL,

        [PricePerFloorM2]    AS (CASE WHEN [FloorM2] > 0 THEN [SalePriceZar] / [FloorM2] END) PERSISTED,
        [Weight]             AS (
            CAST(
                CASE [EvidenceLevel]
                    WHEN 'DeedsVerified'      THEN 1.00
                    WHEN 'OwnTransaction'     THEN 0.90
                    WHEN 'SignedOffer'        THEN 0.80
                    WHEN 'ColleagueConfirmed' THEN 0.55
                    ELSE 0.33
                END
                * CASE [Verification]
                    WHEN 'Verified' THEN 1.00
                    WHEN 'Disputed' THEN 0.00
                    WHEN 'Rejected' THEN 0.00
                    ELSE 0.90
                  END AS DECIMAL (4, 2))) PERSISTED,

        CONSTRAINT [PK_AgentComparables] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [CK_AgentComparables_Price] CHECK ([SalePriceZar] > 0),
        CONSTRAINT [CK_AgentComparables_Evidence] CHECK ([EvidenceLevel] IN
            ('Hearsay', 'ColleagueConfirmed', 'SignedOffer', 'OwnTransaction', 'DeedsVerified')),
        CONSTRAINT [CK_AgentComparables_Verification] CHECK ([Verification] IN
            ('Unverified', 'Verified', 'Disputed', 'Rejected'))
    );

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AgentComparables_Users' AND parent_object_id = OBJECT_ID('dbo.AgentComparables'))
    ALTER TABLE [dbo].[AgentComparables] WITH CHECK
        ADD CONSTRAINT [FK_AgentComparables_Users] FOREIGN KEY ([CapturedByUserId]) REFERENCES [dbo].[Users] ([Id]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentComparables_Area' AND object_id = OBJECT_ID('dbo.AgentComparables'))
    CREATE NONCLUSTERED INDEX [IX_AgentComparables_Area]
        ON [dbo].[AgentComparables] ([Municipality] ASC, [Suburb] ASC, [SaleDate] DESC)
        INCLUDE ([SalePriceZar], [FloorM2], [ErfM2], [Verification]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentComparables_User' AND object_id = OBJECT_ID('dbo.AgentComparables'))
    CREATE NONCLUSTERED INDEX [IX_AgentComparables_User]
        ON [dbo].[AgentComparables] ([CapturedByUserId] ASC, [CapturedAt] DESC);

COMMIT TRANSACTION;
