CREATE TABLE [dbo].[Listing] (
    [Id]                 INT            IDENTITY (1, 1) NOT NULL,
    [ReferenceNumber]    NVARCHAR (100) NULL,
    [P24Ref]             NVARCHAR (100) NULL,
    [PropertyTypeId]     INT            NULL,
    [ListingValuationId] INT            NULL,
    [ListDate]           DATE           NULL,
    [Status]             NVARCHAR (50)  CONSTRAINT [DF__Listing__Status__65C116E7] DEFAULT ('Active') NOT NULL,
    [CreatedAt]          DATETIME2 (7)  CONSTRAINT [DF__Listing__Created__67A95F59] DEFAULT (getutcdate()) NOT NULL,
    [UpdatedAt]          DATETIME2 (7)  CONSTRAINT [DF__Listing__Updated__689D8392] DEFAULT (getutcdate()) NOT NULL,
    CONSTRAINT [PK__Listing__BF3EBED0CE8792AD] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [CK__Listing__Status__66B53B20] CHECK ([Status]='draft' OR [Status]='submitted' OR [Status]='Expired' OR [Status]='Withdrawn' OR [Status]='Let' OR [Status]='Sold' OR [Status]='Active'),
    CONSTRAINT [FK_Listing_ListingValuation] FOREIGN KEY ([ListingValuationId]) REFERENCES [dbo].[ListingValuation] ([Id]),
    CONSTRAINT [FK_Listing_PropertyType] FOREIGN KEY ([PropertyTypeId]) REFERENCES [dbo].[PropertyType] ([Id])
);


GO

