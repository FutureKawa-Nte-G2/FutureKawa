namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for a stored measurement, returned to the frontend or API consumer.
/// </summary>
public record MeasurementResponseDto
{
    public Guid Id { get; init; }
    public Guid WarehouseId { get; init; }
    public string WarehouseName { get; init; } = null!;
    public DateTime MeasDate { get; init; }
    public decimal AvgMeasTemp { get; init; }
    public decimal MaxMeasTemp { get; init; }
    public decimal MinMeasTemp { get; init; }
    public decimal AvgMeasHumidity { get; init; }
    public decimal MinMeasHumidity { get; init; }
    public decimal MaxMeasHumidity { get; init; }
}