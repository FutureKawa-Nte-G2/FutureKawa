namespace FutureKawaSiege.Data.Entities;

/// <summary>
/// Type of alert triggered by temperature or humidity thresholds, or by the
/// country API's own vocabulary (Condition covers both temperature and
/// humidity without distinguishing which one; Expiration concerns a batch).
/// </summary>
public enum AlertType
{
    Temperature,
    Humidity,
    Condition,
    Expiration
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
/// Represents an alert triggered for a warehouse when measurements exceed thresholds.
/// Created by the local warehouse API when temperature or humidity is out of bounds.
/// </summary>
public class Alert
{
    public Guid Id { get; set; }
    
    public Guid WarehouseId { get; set; }
    
    public AlertType Type { get; set; }
    
    public AlertStatus Status { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? ResolvedAt { get; set; }
    
    public DateTime? MeasuredAt { get; set; }

    /// <summary>
    /// The alert's own identifier on the country API that pushed it, used to push
    /// a resolution back to that same country API. Null for alerts that did not
    /// originate from a country push (e.g. seeded data).
    /// </summary>
    public Guid? SourceAlertId { get; set; }

    public Warehouse Warehouse { get; set; } = null!;
}
