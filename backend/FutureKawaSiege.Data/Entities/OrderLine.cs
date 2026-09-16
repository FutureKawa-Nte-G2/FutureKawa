namespace FutureKawaSiege.Data.Entities;

/// <summary>
/// Represents a line item in a sales order.
/// </summary>
public class OrderLine
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string ProductName { get; set; } = null!;
    public decimal Quantity { get; set; }

    public Order Order { get; set; } = null!;
}