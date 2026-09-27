-- Agent profiles for the valuation report pack, and office defaults per agency.
--
-- AgentProfiles: one row per agent (Users.Id), everything the report prints about the agent that
-- Users does not hold: PPRA number, job title, bio, qualifications, website, photo and signature
-- (R2 URLs), the agent's own office details (null = use the agency's), their own brochure pages
-- (null = use the agency's), and their report defaults (calculator rates, room weights) as JSON.
--
-- Agencies gains office defaults: office name, address, phone, email, website, the footer line
-- (e.g. "Directors: … · Each office is independently owned and operated") and brochure pages.
-- An agency has many offices, so these are defaults an agent can override on their profile.
-- Seeded from each agency's own website (2026-09-27); Keller Williams from the KW Dynamic brochure.
--
-- Not applied automatically -- run it once in SSMS or sqlcmd. Safe to re-run.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.AgentProfiles', 'U') IS NULL
    CREATE TABLE [dbo].[AgentProfiles] (
        [UserId]             INT             NOT NULL,
        [AgencySlug]         NVARCHAR (80)   NULL,
        [PpraNumber]         NVARCHAR (40)   NULL,
        [JobTitle]           NVARCHAR (120)  NULL,
        [Bio]                NVARCHAR (2000) NULL,
        [Qualifications]     NVARCHAR (1000) NULL,   -- one per line
        [Website]            NVARCHAR (200)  NULL,
        [PhotoUrl]           NVARCHAR (500)  NULL,
        [SignatureUrl]       NVARCHAR (500)  NULL,
        [OfficeName]         NVARCHAR (150)  NULL,
        [OfficeAddress]      NVARCHAR (300)  NULL,
        [OfficePhone]        NVARCHAR (40)   NULL,
        [OfficeEmail]        NVARCHAR (150)  NULL,
        [OfficeWebsite]      NVARCHAR (200)  NULL,
        [OfficeFooter]       NVARCHAR (400)  NULL,
        [BrochurePagesJson]  NVARCHAR (MAX)  NULL,   -- ["https://…/page1.jpg", …]
        [ReportSettingsJson] NVARCHAR (MAX)  NULL,
        [UpdatedAt]          DATETIME        CONSTRAINT [DF_AgentProfiles_UpdatedAt] DEFAULT (GETUTCDATE()) NOT NULL,
        CONSTRAINT [PK_AgentProfiles] PRIMARY KEY CLUSTERED ([UserId] ASC),
        CONSTRAINT [FK_AgentProfiles_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id])
    );

IF COL_LENGTH('dbo.Agencies', 'OfficeName') IS NULL
    ALTER TABLE [dbo].[Agencies] ADD
        [OfficeName]        NVARCHAR (150) NULL,
        [OfficeAddress]     NVARCHAR (300) NULL,
        [OfficePhone]       NVARCHAR (40)  NULL,
        [OfficeEmail]       NVARCHAR (150) NULL,
        [OfficeWebsite]     NVARCHAR (200) NULL,
        [OfficeFooter]      NVARCHAR (400) NULL,
        [BrochurePagesJson] NVARCHAR (MAX) NULL;

COMMIT TRANSACTION;
GO

-- Defaults only where an agency has none yet, so re-running never overwrites an edit.
UPDATE a SET
    OfficeName    = COALESCE(a.OfficeName, v.OfficeName),
    OfficeAddress = COALESCE(a.OfficeAddress, v.OfficeAddress),
    OfficePhone   = COALESCE(a.OfficePhone, v.OfficePhone),
    OfficeEmail   = COALESCE(a.OfficeEmail, v.OfficeEmail),
    OfficeWebsite = COALESCE(a.OfficeWebsite, v.OfficeWebsite),
    OfficeFooter  = COALESCE(a.OfficeFooter, v.OfficeFooter)
FROM [dbo].[Agencies] a
JOIN (VALUES
    (N'acutts', NULL, N'Shop 5, 6 Village Road, Kloof, 3640', N'+27 31 396 2969', NULL, N'https://www.acutts.co.za', N'Registered with the PPRA'),
    (N'century-21', NULL, N'61 Katherine St, Dennehof, Sandton, 2196', N'+27 11 455 0066', NULL, N'https://www.century21.co.za', N'Deal Design (Pty) Ltd t/a Century 21 South Africa'),
    (N'chas-everitt', NULL, N'126 Kayburn Avenue, Randpark Ridge', N'+27 11 801 2500', NULL, N'https://www.chaseveritt.co.za', N'Registered with the PPRA'),
    (N'engel-volkers', NULL, N'1st Floor, Needwood House, Broadacres Shopping Centre, Cedar Avenue, Fourways, Johannesburg', N'+27 11 465 0410', N'southafrica@engelvoelkers.com', N'https://www.engelvoelkers.com/za/en', N'Engel & Völkers Southern Africa (Pty) Ltd · Master Licence Partner of Engel & Völkers Residential GmbH · Registered with the PPRA'),
    (N'harcourts', NULL, N'Unit 1 Pioneer Campus, 1A Pioneer Road, Kloof, 3610', N'+27 31 201 1060', N'helpdesk@harcourts.co.za', N'https://www.harcourts.co.za', N'Harcourts South Africa (Pty) Ltd t/a Harcourts South Africa · Registered with the PPRA'),
    (N'jawitz', NULL, N'17 Bompas Road, Dunkeld West, Johannesburg, 2196', N'+27 11 880 3550', N'enquiries@jawitz.co.za', N'https://www.jawitz.co.za', N'Jawitz Properties Ltd · Registered with the PPRA'),
    (N'just-property', NULL, N'Sync Unit 15, 253 Main Road, Walmer, Gqeberha, 6065', NULL, NULL, N'https://www.just.property', N'The Just Property Group Holding (Pty) Ltd'),
    (N'keller-williams', N'KW Dynamic', N'51 Reitz St, Audas Estate, Somerset West, 7130', N'+27 21 913 8391', N'kwdynamic@kwsa.co.za', N'https://dynamic.kw.com', N'KELLER WILLIAMS REALTY DYNAMIC · Directors: M C B Barr | M Liebenberg | L H Harding · Each Office is Independently Owned and Operated'),
    (N'leapfrog', NULL, N'99 Jip De Jager Dr, Tygervalley, Cape Town, 7530', NULL, N'info@leapfrogsa.co.za', N'https://leapfrog.co.za', N'Leapfrog Property Group · Registered with the PPRA'),
    (N'lew-geffen', NULL, N'366 Jan Smuts Ave, Craighall, Johannesburg, 2196', N'+27 11 438 7300', NULL, N'https://www.sothebysrealty.co.za', N'Registered with the PPRA'),
    (N'sothebys', NULL, N'366 Jan Smuts Ave, Craighall, Johannesburg, 2196', N'+27 11 438 7300', NULL, N'https://www.sothebysrealty.co.za', N'Registered with the PPRA'),
    (N'meridian', NULL, N'The Woodmill Lifestyle Centre, Loft Office 6, Vredenburg Road, Devonvallei, Stellenbosch, 7600', N'0861 732 589', N'info@meridianiagent.co.za', N'https://www.meridianrealty.co.za', N'Registered with the PPRA'),
    (N'pam-golding', NULL, N'Monterey, 12-14 Klaassens Road, Bishopscourt, Cape Town, 7708', N'+27 21 710 1700', NULL, N'https://www.pamgolding.co.za', N'Registered with the PPRA · Holder of a Business Property Practitioner FFC'),
    (N'property-coza', NULL, N'7 Lourensford Drive, Somerset West', N'+27 12 658 0046', NULL, N'https://www.propertycoza.com', NULL),
    (N'quay-1', NULL, NULL, N'+27 21 426 4848', N'navigatingsuccess@quay1.co.za', N'https://www.quay1.co.za', NULL),
    (N'rawson', NULL, N'222 Main Road, Rondebosch, Cape Town, 7700', N'+27 21 658 7100', NULL, N'https://rawson.co.za', N'Rawson Properties'),
    (N'realtors-international', NULL, NULL, NULL, NULL, N'https://www.realtorsinternational.co.za', NULL),
    (N'remax', NULL, N'39 Tokai Road, Kirstenhof, Cape Town, 7945', N'+27 21 700 2000', N'support@remax.co.za', N'https://www.remax.co.za', N'Each of our offices are independently owned and operated.'),
    (N'seeff', NULL, N'1st Floor, 35 On Rose, 35 Rose Street, Bo-Kaap, Cape Town', N'+27 21 200 0999', NULL, N'https://www.seeff.com', N'Registered with the PPRA'),
    (N'tyson', NULL, N'Office 1, Station Building, Lion Match Office Park, 892 Umgeni Road, Durban, 4001', N'+27 31 583 2700', NULL, N'https://www.tysonprop.co.za', N'Registered with the PPRA')
) AS v (Slug, OfficeName, OfficeAddress, OfficePhone, OfficeEmail, OfficeWebsite, OfficeFooter)
  ON v.Slug = a.Slug;
