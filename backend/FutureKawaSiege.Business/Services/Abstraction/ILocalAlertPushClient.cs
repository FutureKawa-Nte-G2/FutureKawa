namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Pushes alert resolutions back to a country's local warehouse API, closing the
/// loop opened when that same API pushed the alert to <c>POST /api/alerts</c>.
/// </summary>
public interface ILocalAlertPushClient
{
    /// <summary>
    /// Notifies a country API that one of its alerts has been resolved.
    /// </summary>
    /// <param name="countryCode">The country whose API should be called.</param>
    /// <param name="warehouseReference">The warehouse reference, as known by that country API.</param>
    /// <param name="sourceAlertId">The alert's own identifier on that country API.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the country API acknowledged the resolution, false otherwise.</returns>
    Task<bool> PushResolutionAsync(
        string countryCode,
        string warehouseReference,
        Guid sourceAlertId,
        CancellationToken cancellationToken = default);
}
