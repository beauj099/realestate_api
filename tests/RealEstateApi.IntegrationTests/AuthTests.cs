using RealEstateApi.Application.DTOs;

namespace RealEstateApi.IntegrationTests;

[Collection("Database")]
public class AuthTests
{
    private readonly DatabaseFixture _fixture;

    public AuthTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Login_Succeeds_WithValidCredentials()
    {
        await _fixture.ResetAsync();
        var svc = Services.Auth(_fixture);

        var result = await svc.LoginAsync(new LoginRequest("admin", "admin123"));

        Assert.NotNull(result);
        Assert.Equal("Admin", result.Role);
        Assert.Equal("Admin User", result.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
    }

    [Fact]
    public async Task Login_Fails_WithInvalidPassword()
    {
        await _fixture.ResetAsync();
        var svc = Services.Auth(_fixture);

        var result = await svc.LoginAsync(new LoginRequest("admin", "wrong-password"));

        Assert.Null(result);
    }

    [Fact]
    public async Task Login_Fails_ForUnknownUser()
    {
        await _fixture.ResetAsync();
        var svc = Services.Auth(_fixture);

        var result = await svc.LoginAsync(new LoginRequest("ghost", "admin123"));

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshToken_RotatesAndInvalidatesOldToken()
    {
        await _fixture.ResetAsync();
        var svc = Services.Auth(_fixture);

        var login = await svc.LoginAsync(new LoginRequest("agent1", "admin123"));
        Assert.NotNull(login);

        var refreshed = await svc.RefreshTokenAsync(new RefreshTokenRequest(login.RefreshToken));

        Assert.NotNull(refreshed);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.Token));

        var replay = await svc.RefreshTokenAsync(new RefreshTokenRequest(login.RefreshToken));
        Assert.Null(replay);
    }

    [Fact]
    public async Task RefreshToken_Fails_WithUnknownToken()
    {
        await _fixture.ResetAsync();
        var svc = Services.Auth(_fixture);

        var result = await svc.RefreshTokenAsync(new RefreshTokenRequest("not-a-valid-token"));

        Assert.Null(result);
    }
}