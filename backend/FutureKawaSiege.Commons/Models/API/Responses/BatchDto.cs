namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for a coffee batch.
/// </summary>
public record BatchDto
{
    public int Id { get; init; }
    public string Reference { get; init; } = null!;
    public string QualityGrade { get; init; } = null!;
    public string Status { get; init; } = null!;
    public string WarehouseName { get; init; } = null!;
    public string FarmName { get; init; } = null!;
    public string CountryName { get; init; } = null!;
    public DateTime StoredAt { get; init; }
    public DateTime? ShippedAt { get; init; }
}
