using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Pushes batches created at head office, and their later shipment, to a country's
/// local warehouse API, so the country's FIFO and quality tracking see lots that
/// originate from Odoo/head office.
/// </summary>
public interface ILocalBatchPushClient
{
    /// <summary>
    /// Notifies a country API that a batch has been created, via <c>POST /api/batches</c>.
    /// A <c>409 batch_already_exists</c> response (replay) is treated as a success.
    /// </summary>
    /// <returns>True if the country API accepted (or already had) the batch, false otherwise.</returns>
    Task<bool> PushBatchAsync(
        string countryCode,
        string batchReference,
        string farmReference,
        string warehouseReference,
        DateOnly storedAt,
        BatchQualityGrade qualityGrade,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies a country API that a batch has shipped, via
    /// <c>PATCH /api/batches/{batchRef}/ship</c>. Idempotent on the country side.
    /// </summary>
    /// <returns>True if the country API acknowledged the shipment, false otherwise.</returns>
    Task<bool> PushShipmentAsync(
        string countryCode,
        string batchReference,
        DateOnly shippedAt,
        CancellationToken cancellationToken = default);
}
