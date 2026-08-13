CREATE TABLE [dbo].[Listings] (
    [Id]                 INT            IDENTITY (1, 1) NOT NULL,
    [ReferenceNumber]    NVARCHAR (100) NULL,
    [P24Ref]             NVARCHAR (100) NULL,
    [PropertyTypeId]     INT            NULL,
    [ListingValuationId] INT            NULL,
    [ListDate]           DATE           NULL,
    [Status]             NVARCHAR (50)  CONSTRAINT [DF__Listings__Status] DEFAULT ('Active') NOT NULL,
    [CreatedAt]          DATETIME2 (7)  CONSTRAINT [DF__Listings__Created] DEFAULT (getutcdate()) NOT NULL,
    [UpdatedAt]          DATETIME2 (7)  CONSTRAINT [DF__Listings__Updated] DEFAULT (getutcdate()) NOT NULL,
    CONSTRAINT [PK__Listings] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [CK__Listings__Status] CHECK ([Status] IN ('Active', 'Expired', 'Withdrawn', 'Let', 'Sold', 'incomplete', 'submitted')),
    CONSTRAINT [FK_Listings_ListingValuation] FOREIGN KEY ([ListingValuationId]) REFERENCES [dbo].[ListingValuation] ([Id]),
    CONSTRAINT [FK_Listings_PropertyType] FOREIGN KEY ([PropertyTypeId]) REFERENCES [dbo].[PropertyType] ([Id])
);


GO

