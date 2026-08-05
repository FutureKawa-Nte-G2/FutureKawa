namespace FutureKawaSiege.Commons.Models.API.Requests;

/// <summary>
/// Request to update the status of an order.
/// </summary>
public record OrderStatusUpdateDto
{
    public string Status { get; init; } = null!;
}