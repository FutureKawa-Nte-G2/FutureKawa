namespace FutureKawaSiege.Commons.Models.API.Requests;

/// <summary>
/// Request DTO for creating an alert from the local warehouse API.
/// </summary>
public record CreateAlertRequest
{
    public Guid WarehouseId { get; init; }
    public string Type { get; init; } = null!; // "temperature" or "humidity"
    public DateTime? MeasuredAt { get; init; }
}
