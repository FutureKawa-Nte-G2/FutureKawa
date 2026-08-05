namespace FutureKawaSiege.Data.Entities;

/// <summary>
/// Represents a sales order received from Odoo ERP via webhook.
/// </summary>
public class Order
{
    public Guid Id { get; set; }
    public int? OdooOrderId { get; set; }
    public string OrderReference { get; set; } = null!;
    public DateTime OrderDate { get; set; }
    public string ClientName { get; set; } = null!;
    public OrderStatus Status { get; set; }
    public string? IntegrationErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
    public ICollection<Batch> Batches { get; set; } = new List<Batch>();
}

/// <summary>
/// Status of an order in the FutureKawa system.
/// </summary>
public enum OrderStatus
{
    Pending,
    Confirmed,
    Processing,
    Shipped,
    Delivered,
    Error
}