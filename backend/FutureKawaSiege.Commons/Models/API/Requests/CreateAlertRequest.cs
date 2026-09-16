namespace FutureKawaSiege.Commons.Models.API.Requests;

/// <summary>
/// Request DTO for creating an alert from the local warehouse API.
/// </summary>
public record CreateAlertRequest
{
    public string WarehouseReference { get; init; } = null!;
    public string Type { get; init; } = null!; // "temperature", "humidity", "condition" or "expiration"
    public DateTime? MeasuredAt { get; init; }

    /// <summary>
    /// The alert's own identifier on the country API that sent it. Kept so a later
    /// resolution can be pushed back to the same alert on that API. Optional: an
    /// alert pushed without it simply cannot be resolved back to its source.
    /// </summary>
    public Guid? SourceAlertId { get; init; }
}
