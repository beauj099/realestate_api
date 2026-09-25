-- More agencies: the estate agency groups Property24 lists (by advertised listings,
-- pages 1-3), less the ones already present. Duplicates were folded together
-- ("Central Developments ..." x4, "Cosmopolitan Projects ..." x2), and the two with
-- no logo on Property24 (Something Property, PropSolve SA) were left out; agents can
-- still add those via "Other".
-- Also gives Acutts its real colours (it had none to sample before).
-- Logos: tools/SeedAgencyLogos --logos <folder with <slug>.png> --apply.
-- Requires 2026-09-27_agencies.sql. Safe to re-run: existing slugs are left alone,
-- and the Acutts restyle only applies while it still has its original colours.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

INSERT INTO [dbo].[Agencies] ([Slug], [Name], [Monogram], [PrimaryColor], [SecondaryColor], [OnPrimaryColor], [BannerColor])
SELECT v.[Slug], v.[Name], v.[Monogram], v.[PrimaryColor], v.[SecondaryColor], v.[OnPrimaryColor], v.[BannerColor]
FROM (VALUES
        (N'adrienne-hersch', N'Adrienne Hersch Properties', N'AH', N'#C51B24', N'#1E1E1E', N'#FFFFFF', N'#C51B24'),
        (N'aida', N'AIDA', N'AI', N'#E31E24', N'#242424', N'#FFFFFF', N'#FFFFFF'),
        (N'central-developments', N'Central Developments', N'CD', N'#545454', N'#3C3C3C', N'#FFFFFF', N'#FFFFFF'),
        (N'cosmopolitan-projects', N'Cosmopolitan Projects', N'CP', N'#0C3C6C', N'#546C84', N'#FFFFFF', N'#FFFFFF'),
        (N'dg-properties', N'DG Properties', N'DG', N'#111111', N'#C8C4A8', N'#FFFFFF', N'#000000'),
        (N'dormehl-phalane', N'Dormehl Phalane Property Group', N'DP', N'#0C549C', N'#1B8BD0', N'#FFFFFF', N'#FFFFFF'),
        (N'durr-estates', N'Durr Estates', N'DE', N'#0F5E3D', N'#FCCC0C', N'#FFFFFF', N'#0F5E3D'),
        (N'era', N'ERA South Africa', N'ERA', N'#25205F', N'#E4243C', N'#FFFFFF', N'#25205F'),
        (N'exp-south-africa', N'eXp South Africa', N'EXP', N'#111111', N'#4A4A4A', N'#FFFFFF', N'#FFFFFF'),
        (N'fine-and-country', N'Fine & Country', N'FC', N'#1E1E1E', N'#A08C5A', N'#FFFFFF', N'#FFFFFF'),
        (N'fine-home', N'Fine Home', N'FH', N'#CC2424', N'#1E2A5A', N'#FFFFFF', N'#FFFFFF'),
        (N'galetti', N'Galetti Corporate Real Estate', N'GC', N'#F25A24', N'#243C3C', N'#1E1E1E', N'#FFFFFF'),
        (N'gilbert-estates', N'Gilbert Estates', N'GE', N'#4B4B6B', N'#9C9CB4', N'#FFFFFF', N'#FFFFFF'),
        (N'golden-homes', N'Golden Homes', N'GH', N'#F5E400', N'#2A2A10', N'#1E1E1E', N'#F6F6F6'),
        (N'greeff', N'Greeff Christie''s International Real Estate', N'GC', N'#4A4F53', N'#E30613', N'#FFFFFF', N'#4A4F53'),
        (N'greenspace', N'Greenspace Properties', N'GP', N'#3C9C3C', N'#848484', N'#1E1E1E', N'#FFFFFF'),
        (N'huizemark', N'Huizemark', N'HM', N'#F7841C', N'#4A4A4A', N'#1E1E1E', N'#FFFFFF'),
        (N'infoprop', N'Infoprop Real Estate', N'IP', N'#24246C', N'#FCCC0C', N'#FFFFFF', N'#FFFFFF'),
        (N'mandated', N'Mandated Property Group', N'MP', N'#111111', N'#808080', N'#FFFFFF', N'#FFFFFF'),
        (N'maxprop', N'Maxprop', N'MX', N'#FFE600', N'#111111', N'#111111', N'#FFFF00'),
        (N'national-realtors-group', N'National Realtors Group', N'NRG', N'#0C0C54', N'#4A4A8C', N'#FFFFFF', N'#FFFFFF'),
        (N'only-realty', N'Only Realty Property Group', N'OR', N'#071D45', N'#B42454', N'#FFFFFF', N'#071D45'),
        (N'plus-group', N'Plus Group Properties', N'PG', N'#FF6600', N'#1E1E1E', N'#1E1E1E', N'#FF6600'),
        (N'procor', N'Procor S.A.', N'PS', N'#6C0C24', N'#6C6C6C', N'#FFFFFF', N'#FFFFFF'),
        (N'proprop', N'Proprop', N'PP', N'#E40C0C', N'#1E1E1E', N'#FFFFFF', N'#FFFFFF'),
        (N'real-estate-services', N'Real Estate Services', N'RS', N'#3C3C3C', N'#C9963C', N'#FFFFFF', N'#FFFFFF'),
        (N'realnet', N'RealNet Properties', N'RN', N'#CC243C', N'#6C246C', N'#FFFFFF', N'#FFFFFF'),
        (N'realtor-of-excellence', N'Realtor of Excellence', N'RE', N'#2E5E8C', N'#9C9C9C', N'#FFFFFF', N'#FFFFFF'),
        (N'realty-1', N'Realty 1 IPG', N'R1', N'#0C0C3C', N'#0CB4FC', N'#FFFFFF', N'#FFFFFF'),
        (N'sa-property-brokers', N'SA Property Brokers', N'SA', N'#0C3C84', N'#9C9C9C', N'#FFFFFF', N'#FFFFFF'),
        (N'soukop', N'Soukop Property Group International', N'SP', N'#111111', N'#D8D4A8', N'#FFFFFF', N'#000000'),
        (N'thesen-islands-living', N'Thesen Islands Living', N'TI', N'#26384B', N'#84CCCC', N'#FFFFFF', N'#FFFFFF'),
        (N'trafalgar', N'Trafalgar', N'TF', N'#6C0C0C', N'#845454', N'#FFFFFF', N'#FFFFFF'),
        (N'urban-limits', N'Urban Limits Estate Agency', N'UL', N'#D41E33', N'#3C3C3C', N'#FFFFFF', N'#FFFFFF'),
        (N'vered-estates', N'Vered Estates', N'VE', N'#B0261C', N'#3C3C3C', N'#FFFFFF', N'#B0261C'),
        (N'wakefields', N'Wakefields Estate Agents', N'WF', N'#FAE100', N'#D7282F', N'#1E1E1E', N'#FAE100'),
        (N'wykland', N'Wykland Properties', N'WP', N'#E8F20C', N'#111111', N'#111111', N'#EEF651')
    ) AS v ([Slug], [Name], [Monogram], [PrimaryColor], [SecondaryColor], [OnPrimaryColor], [BannerColor])
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Agencies] a WHERE a.[Slug] = v.[Slug]);

UPDATE [dbo].[Agencies]
SET [PrimaryColor] = N'#E4002B', [SecondaryColor] = N'#1E1E1E', [OnPrimaryColor] = N'#FFFFFF',
    [BannerColor] = N'#E4002B', [UpdatedAt] = GETUTCDATE()
WHERE [Slug] = N'acutts' AND [PrimaryColor] = N'#00447C';

-- House brand first, then every listed agency alphabetically.
WITH ordered AS (
    SELECT [SortOrder], ROW_NUMBER() OVER (ORDER BY [Name]) AS rn
    FROM [dbo].[Agencies]
    WHERE [IsCustom] = 0 AND [Slug] <> N'realworth'
)
UPDATE ordered SET [SortOrder] = rn;

COMMIT TRANSACTION;
