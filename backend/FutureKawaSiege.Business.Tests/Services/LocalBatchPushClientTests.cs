using System.Net;
using System.Text;
using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Data.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FutureKawaSiege.Business.Tests.Services;

public class LocalBatchPushClientTests : IDisposable
{
    private const string CountryCode = "BR";
    private const string BaseUrl = "http://local-api.test";

    private readonly CountingHandler _handler = new();
    private readonly LocalBatchPushClient _client;
    private readonly Dictionary<string, string?> _settings = new()
    {
        [$"LocalApi:Countries:{CountryCode}:BaseUrl"] = BaseUrl,
        [$"LocalApi:Countries:{CountryCode}:ApiKey"] = "secret-key",
    };

    public LocalBatchPushClientTests()
    {
        _client = CreateClient();
    }

    public void Dispose()
    {
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task PushBatchAsync_Should_ReturnTrue_When_ApiReturnsCreated()
    {
        _handler.ResponseFactory = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));

        var result = await _client.PushBatchAsync(
            CountryCode, "LOT-001", "FARM-BR-01", "WH-BR-01", new DateOnly(2026, 9, 15), BatchQualityGrade.A);

        Assert.True(result);
        Assert.Equal(1, _handler.CallCount);
    }

    [Fact]
    public async Task PushBatchAsync_Should_ReturnTrue_When_ApiReturnsConflict_BecauseReplayIsSuccess()
    {
        _handler.ResponseFactory = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));

        var result = await _client.PushBatchAsync(
            CountryCode, "LOT-001", "FARM-BR-01", "WH-BR-01", new DateOnly(2026, 9, 15), BatchQualityGrade.A);

        Assert.True(result);
        Assert.Equal(1, _handler.CallCount);
    }

    [Fact]
    public async Task PushBatchAsync_Should_ReturnFalse_WithoutRetry_When_ApiReturnsUnprocessableEntity()
    {
        _handler.ResponseFactory = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"detail":{"code":"farm_ref_unknown"}}""", Encoding.UTF8, "application/json"),
        });

        var result = await _client.PushBatchAsync(
            CountryCode, "LOT-001", "FARM-BR-01", "WH-BR-01", new DateOnly(2026, 9, 15), BatchQualityGrade.A);

        Assert.False(result);
        Assert.Equal(1, _handler.CallCount);
    }

    [Fact]
    public async Task PushBatchAsync_Should_ReturnFalse_WithoutRetry_When_ApiReturnsUnauthorized()
    {
        _handler.ResponseFactory = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await _client.PushBatchAsync(
            CountryCode, "LOT-001", "FARM-BR-01", "WH-BR-01", new DateOnly(2026, 9, 15), BatchQualityGrade.A);

        Assert.False(result);
        Assert.Equal(1, _handler.CallCount);
    }

    [Fact]
    public async Task PushBatchAsync_Should_ReturnFalse_WithoutHttpCall_When_CountryIsNotConfigured()
    {
        var result = await _client.PushBatchAsync(
            "XX", "LOT-001", "FARM-XX-01", "WH-XX-01", new DateOnly(2026, 9, 15), BatchQualityGrade.A);

        Assert.False(result);
        Assert.Equal(0, _handler.CallCount);
    }

    [Fact]
    public async Task PushBatchAsync_Should_Retry_When_ServerErrorIsFollowedBySuccess()
    {
        var callCount = 0;
        _handler.ResponseFactory = (_, _) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(
                callCount == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.Created));
        };

        var result = await _client.PushBatchAsync(
            CountryCode, "LOT-001", "FARM-BR-01", "WH-BR-01", new DateOnly(2026, 9, 15), BatchQualityGrade.A);

        Assert.True(result);
        Assert.Equal(2, _handler.CallCount);
    }

    [Fact]
    public async Task PushShipmentAsync_Should_ReturnTrue_When_ApiReturnsOk()
    {
        _handler.ResponseFactory = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        var result = await _client.PushShipmentAsync(CountryCode, "LOT-001", new DateOnly(2026, 9, 15));

        Assert.True(result);
        Assert.Equal(1, _handler.CallCount);
    }

    [Fact]
    public async Task PushShipmentAsync_Should_ReturnFalse_WithoutRetry_When_ApiReturnsNotFound()
    {
        _handler.ResponseFactory = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"detail":{"code":"batch_not_found"}}""", Encoding.UTF8, "application/json"),
        });

        var result = await _client.PushShipmentAsync(CountryCode, "LOT-001", new DateOnly(2026, 9, 15));

        Assert.False(result);
        Assert.Equal(1, _handler.CallCount);
    }

    [Fact]
    public async Task PushShipmentAsync_Should_ReturnFalse_WithoutHttpCall_When_CountryIsNotConfigured()
    {
        var result = await _client.PushShipmentAsync("XX", "LOT-001", new DateOnly(2026, 9, 15));

        Assert.False(result);
        Assert.Equal(0, _handler.CallCount);
    }

    [Fact]
    public async Task PushShipmentAsync_Should_ReturnFalse_AfterRetries_When_NetworkIsDown()
    {
        _handler.ResponseFactory = (_, _) => throw new HttpRequestException("Connection refused");

        var result = await _client.PushShipmentAsync(CountryCode, "LOT-001", new DateOnly(2026, 9, 15));

        Assert.False(result);
        Assert.Equal(3, _handler.CallCount);
    }

    private LocalBatchPushClient CreateClient()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(_settings)
            .Build();

        return new LocalBatchPushClient(
            new HttpClient(_handler),
            configuration,
            Substitute.For<ILogger<LocalBatchPushClient>>());
    }

    /// <summary>
    /// Stub <see cref="HttpMessageHandler"/> that lets each test decide how to respond
    /// (success, error status, or thrown exception to simulate a network outage).
    /// </summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? ResponseFactory { get; set; }

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;

            if (ResponseFactory is null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return await ResponseFactory(request, cancellationToken);
        }
    }
}
