namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for a warehouse.
/// </summary>
public record WarehouseDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string CountryCode { get; init; } = null!;
}
