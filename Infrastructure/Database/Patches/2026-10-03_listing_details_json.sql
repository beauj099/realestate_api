-- A listing's further details as JSON, owned by the app: ownership type and subtype,
-- construction and views, overall condition, renovations, electricity and water billing,
-- letting, portal references and the mandate information (from the myEdge listing form).
-- And "Vacant Land" as a property type of its own (id 6; slot 4 is Commercial Property), plus
-- the parking types the myEdge form lists.
--
-- Apply BEFORE deploying the API that reads it. Not applied automatically -- run it once in
-- SSMS or sqlcmd. Safe to re-run.

IF COL_LENGTH('dbo.Listings', 'DetailsJson') IS NULL
    ALTER TABLE [dbo].[Listings] ADD [DetailsJson] NVARCHAR(MAX) NULL;

-- Ids are positions in the app's PropertyType list: 6 must be Vacant Land.
IF NOT EXISTS (SELECT 1 FROM dbo.PropertyType WHERE Id = 6)
BEGIN
    IF OBJECTPROPERTY(OBJECT_ID('dbo.PropertyType'), 'TableHasIdentity') = 1
    BEGIN
        SET IDENTITY_INSERT dbo.PropertyType ON;
        INSERT INTO dbo.PropertyType (Id, Name, SortOrder, IsActive) VALUES (6, 'Vacant Land', 6, 1);
        SET IDENTITY_INSERT dbo.PropertyType OFF;
    END
    ELSE
        INSERT INTO dbo.PropertyType (Id, Name, SortOrder, IsActive) VALUES (6, 'Vacant Land', 6, 1);
END;

-- Parking types from the myEdge form (looked up by the app; new rows need no app release).
DECLARE @types TABLE (Description NVARCHAR(100));
INSERT INTO @types VALUES ('Tandem Garage'), ('Secure Parking'), ('Visitors Parking'), ('On-street Parking'), ('Shade Net Parking');
INSERT INTO dbo.ParkingType (Description)
SELECT t.Description FROM @types t
WHERE NOT EXISTS (SELECT 1 FROM dbo.ParkingType p WHERE p.Description = t.Description);
