using FutureKawaSiege.Commons.Models.API.Requests;

namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Service for receiving and managing alerts from the local warehouse API.
/// </summary>
public interface IAlertService
{
    /// <summary>
    /// Receives an alert from the local warehouse API.
    /// Validates the warehouse exists, and ensures idempotency 
    /// (no duplicate active alerts for the same warehouse and type).
    /// </summary>
    Task<bool> ReceiveAlertAsync(CreateAlertRequest request, CancellationToken cancellationToken = default);
}
