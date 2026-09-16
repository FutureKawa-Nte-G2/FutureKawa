namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for the warehouse list endpoint.
/// </summary>
public record WarehouseListResponseDto
{
    public IEnumerable<WarehouseDto> Warehouses { get; init; } = null!;
}
