using FutureKawaSiege.Commons.Models.API.Requests;

namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Service for receiving and managing alerts from the local warehouse API.
/// </summary>
public interface IAlertService
{
    /// <summary>
    /// Receives an alert from the local warehouse API.
    /// Validates the batch and warehouse exist, and ensures idempotency 
    /// (no duplicate active alerts for the same batch and type).
    /// </summary>
    Task<bool> ReceiveAlertAsync(CreateAlertRequest request, CancellationToken cancellationToken = default);
}
