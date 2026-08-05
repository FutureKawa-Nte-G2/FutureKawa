using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Data.Entities;
using Microsoft.Extensions.Configuration;

namespace FutureKawaSiege.Business.Tests.Services;

public class JwtServiceTests
{
    private readonly JwtService _jwtService;

    public JwtServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:AccessTokenLifetimeMinutes"] = "15",
                ["Jwt:RefreshTokenLifetimeDays"] = "7",
                ["Jwt:Secret"] = "test-secret-key-for-unit-tests-32chars!!",
            })
            .Build();

        _jwtService = new JwtService(config);
    }

    private static User CreateTestUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "test@futurekawa.com",
        Role = UserRole.Admin,
        WarehouseId = 1,
    };

    [Fact]
    public void GenerateAccessToken_Should_ReturnNonEmptyString()
    {
        var token = _jwtService.GenerateAccessToken(CreateTestUser());
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void ValidateAccessToken_Should_ReturnPrincipal_When_TokenIsValid()
    {
        var token = _jwtService.GenerateAccessToken(CreateTestUser());
        var principal = _jwtService.ValidateAccessToken(token);
        Assert.NotNull(principal);
    }

    [Fact]
    public void ValidateAccessToken_Should_ReturnNull_When_TokenIsInvalid()
    {
        var principal = _jwtService.ValidateAccessToken("invalid.token.here");
        Assert.Null(principal);
    }

    [Fact]
    public void GenerateRefreshToken_Should_ReturnUniqueTokens()
    {
        var token1 = _jwtService.GenerateRefreshToken();
        var token2 = _jwtService.GenerateRefreshToken();
        Assert.NotEqual(token1, token2);
        Assert.NotEmpty(Convert.FromBase64String(token1));
    }

    [Fact]
    public void GetTokenValidationParameters_Should_ReturnConfiguredParameters()
    {
        var parameters = _jwtService.GetTokenValidationParameters();
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.True(parameters.ValidateLifetime);
        Assert.Equal("TestIssuer", parameters.ValidIssuer);
    }
}
