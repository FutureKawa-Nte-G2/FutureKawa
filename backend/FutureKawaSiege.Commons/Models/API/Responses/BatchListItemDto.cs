namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for a single batch item in the FIFO listing.
/// </summary>
public record BatchListItemDto
{
    public Guid Id { get; init; }
    public string CountryCode { get; init; } = null!;
    public string CountryName { get; init; } = null!;
    public Guid WarehouseId { get; init; }
    public string WarehouseName { get; init; } = null!;
    public string FarmName { get; init; } = null!;
    public string BatchRef { get; init; } = null!;
    public string QualityGrade { get; init; } = null!;
    public string Status { get; init; } = null!;
    public DateTime EnteredAt { get; init; }
    public DateTime? ShippedAt { get; init; }
}
