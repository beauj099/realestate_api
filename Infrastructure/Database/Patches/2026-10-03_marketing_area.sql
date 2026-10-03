-- The area buyers search for a listing by: Property24's name (e.g. Steynsrust) when it differs
-- from the official suburb the City's records use (e.g. Lynn's View). The app suggests it from
-- the Property24 homes nearest the pin; the agent can change it.
--
-- Apply BEFORE deploying the API that reads it. Not applied automatically -- run it once in
-- SSMS or sqlcmd. Safe to re-run.

IF COL_LENGTH('dbo.ListingAddress', 'MarketingArea') IS NULL
    ALTER TABLE [dbo].[ListingAddress] ADD [MarketingArea] NVARCHAR(100) NULL;
