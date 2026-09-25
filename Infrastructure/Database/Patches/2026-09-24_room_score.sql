-- Room score: the agent's overall 0-10 score for each room, kept alongside the
-- existing condition band and averaged by the app into a house score.
-- Not applied automatically -- run once in SSMS or sqlcmd against the deployed
-- database. Safe to re-run: the column is only added when missing.
--
-- Nullable: rooms captured before this change simply have no score yet.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Condition', 'Score') IS NULL
    ALTER TABLE [dbo].[Condition] ADD [Score] DECIMAL(3,1) NULL;

COMMIT TRANSACTION;
