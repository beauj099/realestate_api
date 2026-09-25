-- House score: a percentage (0-100) set by the app/agent per listing, via
-- PUT /api/listings/{id}/house-score, and returned on the listing summary and detail.
-- HouseScoreIsManual records that the agent overrode the app's suggested score.
-- Not applied automatically -- run it once in SSMS or sqlcmd against the deployed
-- database, BEFORE deploying the API build that uses it (every listing query selects
-- these columns). Safe to re-run: each column is only added when missing.
--
-- HouseScore is nullable: listings captured before this change have no score yet.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Listings', 'HouseScore') IS NULL
    ALTER TABLE [dbo].[Listings] ADD [HouseScore] DECIMAL(4,1) NULL;

IF COL_LENGTH('dbo.Listings', 'HouseScoreIsManual') IS NULL
    ALTER TABLE [dbo].[Listings] ADD [HouseScoreIsManual] BIT
        CONSTRAINT [DF_Listings_HouseScoreIsManual] DEFAULT (0) NOT NULL;

COMMIT TRANSACTION;
