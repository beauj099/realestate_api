-- Account deletion with a 90-day grace period: "Delete account" disables the account and records
-- when (DeletionRequestedAt); signing in within 90 days restores it; after that a daily job
-- anonymises the user and deletes their profile (listings and logged sales stay). NULL for every
-- account not being deleted, including ones an admin switched off.
--
-- Apply BEFORE deploying the API that reads it. Not applied automatically -- run it once in SSMS
-- or sqlcmd. Safe to re-run.

IF COL_LENGTH('dbo.Users', 'DeletionRequestedAt') IS NULL
    ALTER TABLE [dbo].[Users] ADD [DeletionRequestedAt] DATETIME2 NULL;
