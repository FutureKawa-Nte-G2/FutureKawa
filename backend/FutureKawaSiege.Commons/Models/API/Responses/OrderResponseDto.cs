namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for a sales order, returned to the frontend or API consumer.
/// </summary>
public record OrderResponseDto
{
    public Guid Id { get; init; }
    public int? OdooOrderId { get; init; }
    public string OrderReference { get; init; } = null!;
    public DateTime OrderDate { get; init; }
    public string ClientName { get; init; } = null!;
    public string Status { get; init; } = null!;
    public string? IntegrationErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public IEnumerable<OrderLineResponseDto> Lines { get; init; } = [];
    public IEnumerable<BatchDto> Batches { get; init; } = [];
}

public record OrderLineResponseDto
{
    public Guid Id { get; init; }
    public string ProductName { get; init; } = null!;
    public decimal Quantity { get; init; }
}