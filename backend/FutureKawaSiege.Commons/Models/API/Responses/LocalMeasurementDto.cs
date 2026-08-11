namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// DTO representing the aggregated measurement response from the local warehouse API.
/// </summary>
public record LocalMeasurementDto
{
    public decimal AvgTemp { get; init; }
    public decimal MaxTemp { get; init; }
    public decimal MinTemp { get; init; }
    public decimal AvgHumidity { get; init; }
    public decimal MaxHumidity { get; init; }
    public decimal MinHumidity { get; init; }
    public DateTime MeasDate { get; init; }
}