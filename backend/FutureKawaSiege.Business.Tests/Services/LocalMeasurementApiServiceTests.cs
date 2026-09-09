using System.Net;
using System.Text;
using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Commons.Models.API.Responses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FutureKawaSiege.Business.Tests.Services;

public class LocalMeasurementApiServiceTests : IDisposable
{
    private const string ApiUrl = "http://local-api.test/measurements";

    private readonly CountingHandler _handler = new();
    private readonly LocalMeasurementApiService _service;
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["MeasurementSync:LocalApiUrl"] = ApiUrl,
        ["MeasurementSync:UseMockData"] = "false",
    };

    public LocalMeasurementApiServiceTests()
    {
        _service = CreateService();
    }

    public void Dispose()
    {
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task FetchMeasurementsAsync_Should_ReturnNull_When_LocalApiIsUnreachable()
    {
        // Simulates a network outage (connection refused, DNS failure, timeout...).
        _handler.ResponseFactory = (_, _) => throw new HttpRequestException("Connection refused");

        var result = await _service.FetchMeasurementsAsync(Guid.NewGuid());

        Assert.Null(result);
        Assert.Equal(1, _handler.CallCount);
    }

    [Fact]
    public async Task FetchMeasurementsAsync_Should_ReturnNull_When_LocalApiReturnsServerError()
    {
        _handler.ResponseFactory = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await _service.FetchMeasurementsAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchMeasurementsAsync_Should_ReturnNull_When_LocalApiReturnsInvalidJson()
    {
        _handler.ResponseFactory = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json"),
        });

        var result = await _service.FetchMeasurementsAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchMeasurementsAsync_Should_ReturnNull_When_LocalApiUrlIsNotConfigured()
    {
        _settings["MeasurementSync:LocalApiUrl"] = null;
        var service = CreateService();

        var result = await service.FetchMeasurementsAsync(Guid.NewGuid());

        Assert.Null(result);
        Assert.Equal(0, _handler.CallCount);
    }

    [Fact]
    public async Task FetchMeasurementsAsync_Should_ReturnDto_When_LocalApiRespondsCorrectly()
    {
        var json = """
        {
            "avgTemp": 25.5,
            "maxTemp": 28.0,
            "minTemp": 23.0,
            "avgHumidity": 61.5,
            "maxHumidity": 65.0,
            "minHumidity": 58.0,
            "measDate": "2026-08-01"
        }
        """;
        _handler.ResponseFactory = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        var result = await _service.FetchMeasurementsAsync(Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Equal(25.5m, result.AvgTemp);
        Assert.Equal(28.0m, result.MaxTemp);
        Assert.Equal(23.0m, result.MinTemp);
        Assert.Equal(61.5m, result.AvgHumidity);
        Assert.Equal(65.0m, result.MaxHumidity);
        Assert.Equal(58.0m, result.MinHumidity);
        Assert.Equal(new DateOnly(2026, 8, 1), result.MeasDate);
    }

    [Fact]
    public async Task FetchMeasurementsAsync_Should_ReturnMockData_WithoutHttpCall_When_UseMockDataIsEnabled()
    {
        _settings["MeasurementSync:UseMockData"] = "true";
        var service = CreateService();

        var result = await service.FetchMeasurementsAsync(Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), result.MeasDate);
        Assert.Equal(0, _handler.CallCount);
    }

    private LocalMeasurementApiService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(_settings)
            .Build();

        return new LocalMeasurementApiService(
            new HttpClient(_handler),
            configuration,
            Substitute.For<ILogger<LocalMeasurementApiService>>());
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
