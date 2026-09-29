-- Office branding for the report pack: a slogan ("we have what it takes to move property
-- forward"), the "Your agent" page heading, and the office's logo variants as JSON
-- ({"mark": url, "wide": url, "wideOnBrand": url}: a square mark, a wide logo for a light
-- background, and a wide logo drawn for the agency colour). On Agencies they are the defaults;
-- on AgentProfiles an office's own (e.g. KW Dynamic's logo under Keller Williams). Empty falls
-- back to the agency's, field by field, in the app.
--
-- Not applied automatically -- run it once in SSMS or sqlcmd. Safe to re-run.

IF COL_LENGTH('dbo.Agencies', 'OfficeSlogan') IS NULL
    ALTER TABLE [dbo].[Agencies] ADD [OfficeSlogan] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Agencies', 'OfficeHeadline') IS NULL
    ALTER TABLE [dbo].[Agencies] ADD [OfficeHeadline] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.Agencies', 'OfficeLogosJson') IS NULL
    ALTER TABLE [dbo].[Agencies] ADD [OfficeLogosJson] NVARCHAR(2000) NULL;

IF COL_LENGTH('dbo.AgentProfiles', 'OfficeSlogan') IS NULL
    ALTER TABLE [dbo].[AgentProfiles] ADD [OfficeSlogan] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.AgentProfiles', 'OfficeHeadline') IS NULL
    ALTER TABLE [dbo].[AgentProfiles] ADD [OfficeHeadline] NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.AgentProfiles', 'OfficeLogosJson') IS NULL
    ALTER TABLE [dbo].[AgentProfiles] ADD [OfficeLogosJson] NVARCHAR(2000) NULL;
