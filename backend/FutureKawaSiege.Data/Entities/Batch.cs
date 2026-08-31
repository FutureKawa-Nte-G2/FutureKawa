namespace FutureKawaSiege.Data.Entities;

/// <summary>
/// Represents a coffee batch stored in a warehouse and originating from a farm.
/// </summary>
public class Batch
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid FarmId { get; set; }
    public string Reference { get; set; } = null!;
    public DateTime StoredAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public BatchQualityGrade QualityGrade { get; set; }
    public BatchStatus Status { get; set; }

    public Warehouse Warehouse { get; set; } = null!;
    public Farm Farm { get; set; } = null!;
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<BatchAlert> Alerts { get; set; } = new List<BatchAlert>();
}

/// <summary>
/// Quality grade of a coffee batch.
/// </summary>
public enum BatchQualityGrade
{
    A,
    B,
    C
}

/// <summary>
/// Status of a coffee batch in the storage lifecycle.
/// </summary>
public enum BatchStatus
{
    Stored,
    Shipped,
    Delivered,
    Expired
}
