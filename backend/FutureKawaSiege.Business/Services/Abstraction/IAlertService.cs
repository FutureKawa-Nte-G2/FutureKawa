using FutureKawaSiege.Commons.Models.API.Requests;

namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Outcome of <see cref="IAlertService.ReceiveAlertAsync"/>, distinct enough for the
/// controller to map each case to its own HTTP status code.
/// </summary>
public enum AlertReceptionResult
{
    Success,
    WarehouseNotFound,
    WarehouseOutOfScope,
    InvalidType
}

/// <summary>
/// Outcome of <see cref="IAlertService.ResolveAlertAsync"/>.
/// </summary>
public enum AlertResolutionResult
{
    Success,
    NotFound
}

/// <summary>
/// Service for receiving and managing alerts from the local warehouse API.
/// </summary>
public interface IAlertService
{
    /// <summary>
    /// Receives an alert from the local warehouse API. Resolves the warehouse by its
    /// external reference, checks it belongs to the country the caller's API key is
    /// scoped to, validates the alert type, and ensures idempotency (no duplicate
    /// active alerts for the same warehouse and type).
    /// </summary>
    /// <param name="request">The alert payload pushed by the country API.</param>
    /// <param name="countryCode">The country code the caller's API key is scoped to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AlertReceptionResult> ReceiveAlertAsync(
        CreateAlertRequest request,
        string countryCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an alert as resolved and pushes the resolution back to the country API
    /// that originally sent it (when it carries a <c>SourceAlertId</c>). Idempotent:
    /// resolving an already-resolved alert succeeds without changing it.
    /// </summary>
    Task<AlertResolutionResult> ResolveAlertAsync(Guid alertId, CancellationToken cancellationToken = default);
}
