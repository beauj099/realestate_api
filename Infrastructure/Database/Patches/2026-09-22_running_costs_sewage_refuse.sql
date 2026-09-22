-- Running costs: replace MunicipalAccount with Sewage + Refuse.
-- Run once against the deployed database (SSMS or sqlcmd). Safe to re-run:
-- every step checks COL_LENGTH first.
--
-- Decimal precision matches the existing columns (decimal(12,2) per
-- INFORMATION_SCHEMA on realworthdb, 2026-09-22). Pre-migration check showed
-- 4 rows, 0 non-null MunicipalAccount values, so no data is lost by the drop.
-- A backup is taken first as PropertyRunningCosts_backup_20260922.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.PropertyRunningCosts_backup_20260922', 'U') IS NULL
    SELECT * INTO dbo.PropertyRunningCosts_backup_20260922 FROM dbo.PropertyRunningCosts;

IF COL_LENGTH('dbo.PropertyRunningCosts', 'MunicipalAccount') IS NOT NULL
    ALTER TABLE [dbo].[PropertyRunningCosts] DROP COLUMN [MunicipalAccount];

IF COL_LENGTH('dbo.PropertyRunningCosts', 'Sewage') IS NULL
    ALTER TABLE [dbo].[PropertyRunningCosts] ADD [Sewage] DECIMAL(12,2) NULL;

IF COL_LENGTH('dbo.PropertyRunningCosts', 'Refuse') IS NULL
    ALTER TABLE [dbo].[PropertyRunningCosts] ADD [Refuse] DECIMAL(12,2) NULL;

COMMIT TRANSACTION;
