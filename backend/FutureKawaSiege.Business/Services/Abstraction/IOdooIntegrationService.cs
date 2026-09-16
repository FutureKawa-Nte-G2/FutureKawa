namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Service for communicating with the Odoo ERP via JSON-RPC.
/// </summary>
public interface IOdooIntegrationService
{
    /// <summary>
    /// Authenticates with the Odoo instance and returns the user ID.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The Odoo user ID (uid) used for subsequent API calls.</returns>
    Task<int> AuthenticateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the integration status of an order in Odoo by calling
    /// the <c>action_mark_shipped</c> method on <c>sale.order</c>.
    /// </summary>
    /// <param name="odooOrderId">The Odoo order ID to update.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns><c>true</c> if the update succeeded; otherwise <c>false</c>.</returns>
    Task<bool> NotifyOrderShippedAsync(int odooOrderId, CancellationToken cancellationToken = default);
}