-- A flatlet's own layout on its room, every figure the report pack uses on the listing's
-- valuation, and the owners' bond.
--
--   ListingRoom.UnitDetails        JSON from the app: bedrooms, bathrooms, kitchens, lounges,
--                                   own entrance / meter / parking, let out and the rent.
--   ListingValuation.ValueLowZar … the agent's range and asking price, why the range differs
--                                   from the sales, and this listing's commission and bond
--                                   figures (null = the agent's Report settings).
--   ListingValuation.BondInstitution, BondAmountZar   the owners' bond, as they tell the agent.
--
-- Apply BEFORE deploying the API that reads them. Not applied automatically -- run it once in
-- SSMS or sqlcmd. Safe to re-run.

IF COL_LENGTH('dbo.ListingRoom', 'UnitDetails') IS NULL
    ALTER TABLE [dbo].[ListingRoom] ADD [UnitDetails] NVARCHAR(4000) NULL;

IF COL_LENGTH('dbo.ListingValuation', 'ValueLowZar') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [ValueLowZar] DECIMAL(18, 2) NULL;
IF COL_LENGTH('dbo.ListingValuation', 'ValueHighZar') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [ValueHighZar] DECIMAL(18, 2) NULL;
IF COL_LENGTH('dbo.ListingValuation', 'ListingPriceZar') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [ListingPriceZar] DECIMAL(18, 2) NULL;
IF COL_LENGTH('dbo.ListingValuation', 'AdjustmentReason') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [AdjustmentReason] NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.ListingValuation', 'CommissionLatePercent') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [CommissionLatePercent] DECIMAL(5, 2) NULL;
IF COL_LENGTH('dbo.ListingValuation', 'CommissionEarlyMonths') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [CommissionEarlyMonths] INT NULL;
IF COL_LENGTH('dbo.ListingValuation', 'CommissionIncludesVat') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [CommissionIncludesVat] BIT NULL;
IF COL_LENGTH('dbo.ListingValuation', 'InterestRatePercent') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [InterestRatePercent] DECIMAL(5, 2) NULL;
IF COL_LENGTH('dbo.ListingValuation', 'BondTermYears') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [BondTermYears] INT NULL;
IF COL_LENGTH('dbo.ListingValuation', 'DepositPercent') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [DepositPercent] DECIMAL(5, 2) NULL;
IF COL_LENGTH('dbo.ListingValuation', 'BondInstitution') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [BondInstitution] NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.ListingValuation', 'BondAmountZar') IS NULL
    ALTER TABLE [dbo].[ListingValuation] ADD [BondAmountZar] DECIMAL(18, 2) NULL;
