using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Data.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

/// <summary>
/// Calls a country's local warehouse API to push batches created at head office and
/// their later shipment. Each country is configured under <c>LocalApi:Countries:{code}</c>
/// with its own <c>ApiKey</c> and <c>BaseUrl</c>, the same as <see cref="LocalAlertPushClient"/>.
///
/// Transient failures (network errors, 5xx, 408, 429) are retried a couple of times with
/// a short backoff. Deterministic rejections (401, 404, 409, 422) are not retried: retrying
/// them would not change the outcome without a config or referential fix.
/// </summary>
public class LocalBatchPushClient : ILocalBatchPushClient
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
    ];

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalBatchPushClient> _logger;

    public LocalBatchPushClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<LocalBatchPushClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> PushBatchAsync(
        string countryCode,
        string batchReference,
        string farmReference,
        string warehouseReference,
        DateOnly storedAt,
        BatchQualityGrade qualityGrade,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCountryConfig(countryCode, out var baseUrl, out var apiKey))
        {
            _logger.LogWarning(
                "LocalApi:Countries:{CountryCode} is not fully configured. Skipping batch push for {BatchReference}.",
                countryCode, batchReference);
            return false;
        }

        var endpoint = $"{baseUrl.TrimEnd('/')}/api/batches";
        var payload = new BatchCreateRequest
        {
            BatchRef = batchReference,
            FarmRef = farmReference,
            WarehouseRef = warehouseReference,
            StoredAt = storedAt,
            QualityGrade = qualityGrade.ToString(),
        };

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = JsonContent.Create(payload),
                };
                requestMessage.Headers.Add("X-API-Key", apiKey);

                var response = await _httpClient.SendAsync(requestMessage, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Created)
                {
                    _logger.LogInformation(
                        "Pushed batch {BatchReference} to country {CountryCode}.",
                        batchReference, countryCode);
                    return true;
                }

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    _logger.LogInformation(
                        "Batch {BatchReference} already exists on country {CountryCode} (replay); treating as success.",
                        batchReference, countryCode);
                    return true;
                }

                if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError(
                        "Country {CountryCode} rejected batch {BatchReference} as invalid (422): {Body}. Not retrying, referential data must be corrected.",
                        countryCode, batchReference, body);
                    return false;
                }

                if (!IsTransient(response.StatusCode) || attempt >= RetryDelays.Length)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError(
                        "Failed to push batch {BatchReference} to country {CountryCode} at {Endpoint}: {StatusCode} {Body}",
                        batchReference, countryCode, endpoint, (int)response.StatusCode, body);
                    return false;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= RetryDelays.Length)
                {
                    _logger.LogError(ex,
                        "Failed to push batch {BatchReference} to country {CountryCode} at {Endpoint}.",
                        batchReference, countryCode, endpoint);
                    return false;
                }
            }

            await Task.Delay(RetryDelays[attempt], cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> PushShipmentAsync(
        string countryCode,
        string batchReference,
        DateOnly shippedAt,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCountryConfig(countryCode, out var baseUrl, out var apiKey))
        {
            _logger.LogWarning(
                "LocalApi:Countries:{CountryCode} is not fully configured. Skipping shipment push for batch {BatchReference}.",
                countryCode, batchReference);
            return false;
        }

        var endpoint = $"{baseUrl.TrimEnd('/')}/api/batches/{Uri.EscapeDataString(batchReference)}/ship";
        var payload = new BatchShipRequest { ShippedAt = shippedAt };

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var requestMessage = new HttpRequestMessage(HttpMethod.Patch, endpoint)
                {
                    Content = JsonContent.Create(payload),
                };
                requestMessage.Headers.Add("X-API-Key", apiKey);

                var response = await _httpClient.SendAsync(requestMessage, cancellationToken);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    _logger.LogInformation(
                        "Pushed shipment of batch {BatchReference} to country {CountryCode}.",
                        batchReference, countryCode);
                    return true;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var notFoundBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError(
                        "Country {CountryCode} does not know batch {BatchReference} (404): {Body}. The initial batch push likely failed.",
                        countryCode, batchReference, notFoundBody);
                    return false;
                }

                if (!IsTransient(response.StatusCode) || attempt >= RetryDelays.Length)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError(
                        "Failed to push shipment of batch {BatchReference} to country {CountryCode} at {Endpoint}: {StatusCode} {Body}",
                        batchReference, countryCode, endpoint, (int)response.StatusCode, body);
                    return false;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= RetryDelays.Length)
                {
                    _logger.LogError(ex,
                        "Failed to push shipment of batch {BatchReference} to country {CountryCode} at {Endpoint}.",
                        batchReference, countryCode, endpoint);
                    return false;
                }
            }

            await Task.Delay(RetryDelays[attempt], cancellationToken);
        }
    }

    private bool TryGetCountryConfig(string countryCode, out string baseUrl, out string apiKey)
    {
        var countrySection = _configuration.GetSection($"LocalApi:Countries:{countryCode}");
        baseUrl = countrySection["BaseUrl"] ?? string.Empty;
        apiKey = countrySection["ApiKey"] ?? string.Empty;

        return !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(apiKey);
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private sealed class BatchCreateRequest
    {
        [JsonPropertyName("batchRef")]
        public string BatchRef { get; set; } = null!;

        [JsonPropertyName("farmRef")]
        public string FarmRef { get; set; } = null!;

        [JsonPropertyName("warehouseRef")]
        public string WarehouseRef { get; set; } = null!;

        [JsonPropertyName("storedAt")]
        public DateOnly StoredAt { get; set; }

        [JsonPropertyName("qualityGrade")]
        public string QualityGrade { get; set; } = null!;
    }

    private sealed class BatchShipRequest
    {
        [JsonPropertyName("shippedAt")]
        public DateOnly ShippedAt { get; set; }
    }
}
