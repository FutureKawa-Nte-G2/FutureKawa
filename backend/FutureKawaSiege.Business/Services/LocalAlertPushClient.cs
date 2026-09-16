using FutureKawaSiege.Business.Services.Abstraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

/// <summary>
/// Calls a country's local warehouse API back to resolve an alert it previously
/// pushed to us. Each country is configured under <c>LocalApi:Countries:{code}</c>
/// with its own <c>ApiKey</c> (the same one it uses to authenticate its pushes) and
/// <c>BaseUrl</c>.
/// </summary>
public class LocalAlertPushClient : ILocalAlertPushClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalAlertPushClient> _logger;

    public LocalAlertPushClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<LocalAlertPushClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> PushResolutionAsync(
        string countryCode,
        string warehouseReference,
        Guid sourceAlertId,
        CancellationToken cancellationToken = default)
    {
        var countrySection = _configuration.GetSection($"LocalApi:Countries:{countryCode}");
        var baseUrl = countrySection["BaseUrl"];
        var apiKey = countrySection["ApiKey"];

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "LocalApi:Countries:{CountryCode} is not fully configured. Skipping resolution push for alert {SourceAlertId}.",
                countryCode, sourceAlertId);
            return false;
        }

        var endpoint = $"{baseUrl.TrimEnd('/')}/api/alerts/{sourceAlertId}/resolve" +
            $"?warehouse_ref={Uri.EscapeDataString(warehouseReference)}";

        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Patch, endpoint);
            requestMessage.Headers.Add("X-API-Key", apiKey);

            var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation(
                "Pushed resolution of alert {SourceAlertId} to country {CountryCode}.",
                sourceAlertId, countryCode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to push resolution of alert {SourceAlertId} to country {CountryCode} at {Endpoint}.",
                sourceAlertId, countryCode, endpoint);
            return false;
        }
    }
}
