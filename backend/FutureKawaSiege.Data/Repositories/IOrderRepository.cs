using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Data.Repositories;

public interface IOrderRepository
{
    /// <summary>
    /// Retrieves all orders with their lines.
    /// </summary>
    Task<IEnumerable<Order>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single order by its internal GUID, including lines.
    /// </summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single order by its Odoo order ID, including lines.
    /// </summary>
    Task<Order?> GetByOdooOrderIdAsync(int odooOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new order to the data store.
    /// </summary>
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing order entity in the data store.
    /// </summary>
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
}