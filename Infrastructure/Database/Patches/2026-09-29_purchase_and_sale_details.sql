-- The owners' last purchase on a listing's valuation (as they tell the agent: the City's sales
-- record only reaches back a few years), and a pool on agent-reported sales.
--
-- Apply BEFORE deploying the API that reads them. Not applied automatically -- run it once in
-- SSMS or sqlcmd. Safe to re-run.

IF COL_LENGTH('dbo.ListingValuation', 'LastPurchaseDate') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [LastPurchaseDate] DATE NULL;
IF COL_LENGTH('dbo.ListingValuation', 'LastPurchasePriceZar') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [LastPurchasePriceZar] DECIMAL(18, 2) NULL;

IF COL_LENGTH('dbo.AgentComparables', 'HasPool') IS NULL
    ALTER TABLE [dbo].[AgentComparables] ADD [HasPool] BIT NULL;
