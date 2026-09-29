-- The report's heading font per agency (and per office): "serif" sets the report pack's
-- headings (cover title, "Specially prepared for", slogans, the "Your agent" heading and the
-- section titles) in a serif, for brands with a serif identity; NULL keeps the body font (Lato).
--
-- Apply BEFORE deploying the API that reads it: the agency list selects this column.
-- Not applied automatically -- run it once in SSMS or sqlcmd. Safe to re-run.

IF COL_LENGTH('dbo.Agencies', 'OfficeHeadingFont') IS NULL
    ALTER TABLE [dbo].[Agencies] ADD [OfficeHeadingFont] NVARCHAR(20) NULL;
IF COL_LENGTH('dbo.AgentProfiles', 'OfficeHeadingFont') IS NULL
    ALTER TABLE [dbo].[AgentProfiles] ADD [OfficeHeadingFont] NVARCHAR(20) NULL;
GO

-- Pam Golding's identity is a serif.
UPDATE [dbo].[Agencies] SET [OfficeHeadingFont] = 'serif' WHERE [Slug] = 'pam-golding' AND [OfficeHeadingFont] IS NULL;
