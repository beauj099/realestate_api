CREATE TABLE [dbo].[PropertyRunningCosts] (
    [Id]           INT             IDENTITY (1, 1) NOT NULL,
    [ListingId]    INT             NULL,
    [MonthlyLevy]  DECIMAL (12, 2) NULL,
    [MonthlyRates] DECIMAL (12, 2) NULL,
    [Electricity]  DECIMAL (12, 2) NULL,
    [Water]        DECIMAL (12, 2) NULL,
    CONSTRAINT [PK_PropertyExpenses] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_PropertyRunningCosts_Listing] FOREIGN KEY ([ListingId]) REFERENCES [dbo].[Listings] ([Id]),
    -- sp_PropertyRunningCosts_Upsert assumes at most one row per listing; enforce it.
    CONSTRAINT [UX_PropertyRunningCosts_ListingId] UNIQUE NONCLUSTERED ([ListingId] ASC)
);


GO

