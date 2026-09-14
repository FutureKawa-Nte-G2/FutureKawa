using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Sends email notifications when an alert is received. Failures are logged and
/// swallowed by the implementation: a broken mail server must never prevent an
/// alert from being recorded.
/// </summary>
public interface IAlertEmailService
{
    /// <summary>
    /// Sends a notification email for a newly created alert. <paramref name="alert"/>
    /// must have its <see cref="Alert.Warehouse"/> (and <see cref="Warehouse.Country"/>)
    /// navigation properties populated, since they are used in the email content.
    /// </summary>
    Task SendAlertCreatedNotificationAsync(Alert alert, CancellationToken cancellationToken = default);
}
