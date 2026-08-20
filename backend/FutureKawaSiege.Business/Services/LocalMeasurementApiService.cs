using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Responses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

/// <summary>
/// Service for fetching aggregated measurements from the local warehouse API.
///
/// The API URL is read from configuration (<c>MeasurementSync:LocalApiUrl</c>) as a fixed
/// value for this PoC (single country). The URL is used as-is (full endpoint URL).
/// When <c>MeasurementSync:UseMockData</c> is true, the service generates fake data
/// directly without making an HTTP call — useful for testing when no local API exists.
/// </summary>
public class LocalMeasurementApiService : ILocalMeasurementApiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalMeasurementApiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Random Rng = new();

    public LocalMeasurementApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<LocalMeasurementApiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<LocalMeasurementDto?> FetchMeasurementsAsync(Guid warehouseId, CancellationToken cancellationToken = default)
    {
        var useMockData = _configuration.GetValue<bool>("MeasurementSync:UseMockData");

        if (useMockData)
        {
            _logger.LogInformation("UseMockData is enabled — generating fake measurement data for warehouse {WarehouseId}", warehouseId);
            return GenerateMockData();
        }

        var baseUrl = _configuration["MeasurementSync:LocalApiUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("MeasurementSync:LocalApiUrl is not configured. Skipping measurement fetch for warehouse {WarehouseId}.", warehouseId);
            return null;
        }

        var endpoint = baseUrl.TrimEnd('/');

        try
        {
            _logger.LogInformation("Fetching measurements from local API for warehouse {WarehouseId} at {Endpoint}", warehouseId, endpoint);

            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var dto = JsonSerializer.Deserialize<LocalMeasurementDto>(content, JsonOptions);

            if (dto is null)
            {
                _logger.LogWarning("Local API returned null or invalid measurement data for warehouse {WarehouseId}.", warehouseId);
                return null;
            }

            _logger.LogInformation("Successfully fetched measurements for warehouse {WarehouseId} (date: {MeasDate})", warehouseId, dto.MeasDate);
            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch measurements from local API for warehouse {WarehouseId} at {Endpoint}", warehouseId, endpoint);
            return null;
        }
    }

    /// <summary>
    /// Generates fake aggregated measurement data with small random variations
    /// around typical coffee storage conditions (25°C, 60% humidity).
    /// </summary>
    private static LocalMeasurementDto GenerateMockData()
    {
        var baseTemp = 25.0m;
        var tempVariance = (decimal)Math.Round(Rng.NextDouble() * 4 - 2, 2);
        var baseHumidity = 60.0m;
        var humidityVariance = (decimal)Math.Round(Rng.NextDouble() * 6 - 3, 2);

        return new LocalMeasurementDto
        {
            AvgTemp = baseTemp + tempVariance,
            MaxTemp = baseTemp + tempVariance + 2.5m,
            MinTemp = baseTemp + tempVariance - 2.5m,
            AvgHumidity = baseHumidity + humidityVariance,
            MaxHumidity = baseHumidity + humidityVariance + 4.0m,
            MinHumidity = baseHumidity + humidityVariance - 4.0m,
            MeasDate = DateOnly.FromDateTime(DateTime.UtcNow),
        };
    }
}