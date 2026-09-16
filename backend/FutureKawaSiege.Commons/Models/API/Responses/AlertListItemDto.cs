namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for a single alert item in the alerts listing.
/// </summary>
public record AlertListItemDto
{
    public Guid Id { get; init; }
    public Guid WarehouseId { get; init; }
    public string WarehouseName { get; init; } = null!;
    public string CountryCode { get; init; } = null!;
    public string CountryName { get; init; } = null!;
    public string Type { get; init; } = null!;
    public string Status { get; init; } = null!;
    public DateTime CreatedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public DateTime? MeasuredAt { get; init; }
}
