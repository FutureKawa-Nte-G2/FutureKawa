namespace FutureKawaSiege.Data.Entities;

/// <summary>
/// Represents an aggregated measurement record for a warehouse on a given date.
///
/// Stores the average, minimum, and maximum values for temperature and humidity
/// as reported by the local warehouse API. This allows the Head Office backend
/// to retain a historical record of storage conditions even when the connection
/// to the country is lost.
/// </summary>
public class Measurement
{
    /// <summary>
    /// Unique identifier for the measurement record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the warehouse this measurement belongs to.
    /// </summary>
    public Guid WarehouseId { get; set; }

    /// <summary>
    /// Date of the measurement (aggregated for that day).
    /// </summary>
    public DateTime MeasDate { get; set; }

    /// <summary>
    /// Average temperature measured (°C).
    /// </summary>
    public decimal AvgMeasTemp { get; set; }

    /// <summary>
    /// Maximum temperature measured (°C).
    /// </summary>
    public decimal MaxMeasTemp { get; set; }

    /// <summary>
    /// Minimum temperature measured (°C).
    /// </summary>
    public decimal MinMeasTemp { get; set; }

    /// <summary>
    /// Average humidity measured (%).
    /// </summary>
    public decimal AvgMeasHumidity { get; set; }

    /// <summary>
    /// Minimum humidity measured (%).
    /// </summary>
    public decimal MinMeasHumidity { get; set; }

    /// <summary>
    /// Maximum humidity measured (%).
    /// </summary>
    public decimal MaxMeasHumidity { get; set; }

    /// <summary>
    /// Navigation property to the warehouse this measurement belongs to.
    /// </summary>
    public Warehouse Warehouse { get; set; } = null!;
}