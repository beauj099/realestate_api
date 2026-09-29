-- An owner's title (Mr, Mrs, Ms, Dr, Prof, Adv, ...), optional. The report pack's letter
-- greets owners by title when they have one ("Dear Mr & Mrs Swanepoel"); the cover always
-- uses their names.
--
-- Not applied automatically -- run it once in SSMS or sqlcmd. Safe to re-run.

IF COL_LENGTH('dbo.Contact', 'Title') IS NULL
    ALTER TABLE [dbo].[Contact] ADD [Title] NVARCHAR(20) NULL;
