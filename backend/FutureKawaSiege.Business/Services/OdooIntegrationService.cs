using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FutureKawaSiege.Business.Services.Abstraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

/// <summary>
/// Service for communicating with the Odoo ERP via JSON-RPC.
///
/// Uses the standard Odoo web service API:
/// 1. Authenticate via the "common" service to obtain a user ID (uid)
/// 2. Execute model methods via the "object" service (execute_kw)
///
/// All calls are sent to the Odoo /jsonrpc endpoint as JSON-RPC 2.0 payloads.
/// </summary>
public class OdooIntegrationService : IOdooIntegrationService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OdooIntegrationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public OdooIntegrationService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OdooIntegrationService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var db = _configuration["Odoo:Db"]
            ?? throw new InvalidOperationException("Odoo:Db not configured.");
        var login = _configuration["Odoo:Username"]
            ?? throw new InvalidOperationException("Odoo:Username not configured.");
        var password = _configuration["Odoo:Password"]
            ?? throw new InvalidOperationException("Odoo:Password not configured.");

        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "call",
            @params = new
            {
                service = "common",
                method = "login",
                args = new object[] { db, login, password }
            }
        };

        var response = await SendJsonRpcAsync(requestBody, cancellationToken);

        // The result is the user ID (integer) or false if auth failed
        if (response.ValueKind == JsonValueKind.False)
            throw new InvalidOperationException("Odoo authentication failed: invalid credentials.");

        var uid = response.GetInt32();
        _logger.LogInformation("Odoo authentication successful, uid: {Uid}", uid);
        return uid;
    }

    /// <inheritdoc/>
    public async Task<bool> NotifyOrderShippedAsync(int odooOrderId, CancellationToken cancellationToken = default)
    {
        var db = _configuration["Odoo:Db"]
            ?? throw new InvalidOperationException("Odoo:Db not configured.");
        var password = _configuration["Odoo:Password"]
            ?? throw new InvalidOperationException("Odoo:Password not configured.");

        _logger.LogInformation(
            "FutureKawa [SYNC→]: ──────────────────────────────────────────────");
        _logger.LogInformation(
            "FutureKawa [SYNC→]: Démarrage notification expédition vers Odoo (OrderId={OdooOrderId})",
            odooOrderId);

        // 1. Authenticate to get the uid
        _logger.LogInformation("FutureKawa [SYNC→]: Authentification JSON-RPC vers Odoo...");
        var uid = await AuthenticateAsync(cancellationToken);
        _logger.LogInformation(
            "FutureKawa [SYNC→]: Authentification réussie, uid={Uid}", uid);

        // 2. Call sale.order action_mark_shipped via execute_kw
        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "call",
            @params = new
            {
                service = "object",
                method = "execute_kw",
                args = new object[]
                {
                    db,
                    uid,
                    password,
                    "sale.order",
                    "action_mark_shipped",
                    new[] { new[] { odooOrderId } }
                }
            }
        };

        _logger.LogInformation(
            "FutureKawa [SYNC→]: Appel execute_kw → sale.order.action_mark_shipped([{OdooOrderId}])",
            odooOrderId);

        try
        {
            var response = await SendJsonRpcAsync(requestBody, cancellationToken);
            _logger.LogInformation(
                "FutureKawa [SYNC→]: Réponse Odoo: {Response}",
                response.ValueKind == JsonValueKind.True ? "true" : response.ToString());
            _logger.LogInformation(
                "FutureKawa [SYNC→]: ✅ Commande Odoo {OdooOrderId} marquée comme expédiée",
                odooOrderId);
            _logger.LogInformation(
                "FutureKawa [SYNC→]: ──────────────────────────────────────────────");
            return response.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to notify Odoo that order {OdooOrderId} was shipped",
                odooOrderId);
            return false;
        }
    }

    /// <summary>
    /// Sends a JSON-RPC 2.0 request to the Odoo /jsonrpc endpoint
    /// and extracts the "result" field from the response.
    /// </summary>
    private async Task<JsonElement> SendJsonRpcAsync(object requestBody, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var odooUrl = _configuration["Odoo:Url"]
            ?? throw new InvalidOperationException("Odoo:Url not configured.");
        var endpoint = odooUrl.TrimEnd('/') + "/jsonrpc";

        var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("error", out var errorEl) && errorEl.ValueKind != JsonValueKind.Null)
        {
            var errorMsg = errorEl.ToString();
            _logger.LogError("Odoo JSON-RPC error: {Error}", errorMsg);
            throw new InvalidOperationException($"Odoo JSON-RPC error: {errorMsg}");
        }

        // Odoo may omit "result" when the called method returns None/void
        if (doc.RootElement.TryGetProperty("result", out var resultEl))
            return resultEl.Clone();

        // No error and no result → the call succeeded (method returned None)
        return JsonSerializer.SerializeToElement(true);
    }
}