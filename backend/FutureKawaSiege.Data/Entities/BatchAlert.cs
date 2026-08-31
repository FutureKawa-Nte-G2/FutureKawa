namespace FutureKawaSiege.Data.Entities;

/// <summary>
/// Type of alert triggered by temperature or humidity thresholds.
/// </summary>
public enum AlertType
{
    Temperature,
    Humidity
}

/// <summary>
/// Status of an alert: active when triggered, resolved when acknowledged or cleared.
/// </summary>
public enum AlertStatus
{
    Active,
    Resolved
}

/// <summary>
/// Represents an alert triggered for a batch when measurements exceed thresholds.
/// Created by the local warehouse API when temperature or humidity is out of bounds.
/// </summary>
public class BatchAlert
{
    public Guid Id { get; set; }
    
    public Guid WarehouseId { get; set; }
    
    public Guid BatchId { get; set; }
    
    public AlertType Type { get; set; }
    
    public AlertStatus Status { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? ResolvedAt { get; set; }
    
    public DateTime? MeasuredAt { get; set; }

    public Warehouse Warehouse { get; set; } = null!;
    public Batch Batch { get; set; } = null!;
}
