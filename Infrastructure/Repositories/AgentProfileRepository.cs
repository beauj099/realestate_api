using Dapper;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Infrastructure.Repositories;

/// <summary>dbo.AgentProfiles: one row per agent, created on first save.</summary>
public class AgentProfileRepository(DbConnectionFactory connectionFactory)
{
    public async Task<AgentProfile?> GetAsync(int userId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<AgentProfile>(new CommandDefinition(
            "SELECT * FROM AgentProfiles WHERE UserId = @UserId", new { UserId = userId }, cancellationToken: ct));
    }

    /// <summary>Inserts or replaces the whole row.</summary>
    public async Task SaveAsync(AgentProfile p, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            @"MERGE AgentProfiles AS t
              USING (SELECT @UserId AS UserId) AS s ON t.UserId = s.UserId
              WHEN MATCHED THEN UPDATE SET
                  AgencySlug = @AgencySlug, PpraNumber = @PpraNumber, JobTitle = @JobTitle, Bio = @Bio,
                  Qualifications = @Qualifications, Website = @Website, PhotoUrl = @PhotoUrl,
                  SignatureUrl = @SignatureUrl, OfficeName = @OfficeName, OfficeAddress = @OfficeAddress,
                  OfficePhone = @OfficePhone, OfficeEmail = @OfficeEmail, OfficeWebsite = @OfficeWebsite,
                  OfficeFooter = @OfficeFooter, OfficeSlogan = @OfficeSlogan, OfficeHeadline = @OfficeHeadline,
                  OfficeLogosJson = @OfficeLogosJson, BrochurePagesJson = @BrochurePagesJson,
                  ReportSettingsJson = @ReportSettingsJson, UpdatedAt = GETUTCDATE()
              WHEN NOT MATCHED THEN INSERT
                  (UserId, AgencySlug, PpraNumber, JobTitle, Bio, Qualifications, Website, PhotoUrl, SignatureUrl,
                   OfficeName, OfficeAddress, OfficePhone, OfficeEmail, OfficeWebsite, OfficeFooter,
                   OfficeSlogan, OfficeHeadline, OfficeLogosJson, BrochurePagesJson, ReportSettingsJson)
                  VALUES
                  (@UserId, @AgencySlug, @PpraNumber, @JobTitle, @Bio, @Qualifications, @Website, @PhotoUrl, @SignatureUrl,
                   @OfficeName, @OfficeAddress, @OfficePhone, @OfficeEmail, @OfficeWebsite, @OfficeFooter,
                   @OfficeSlogan, @OfficeHeadline, @OfficeLogosJson, @BrochurePagesJson, @ReportSettingsJson);",
            p, cancellationToken: ct));
    }
}
