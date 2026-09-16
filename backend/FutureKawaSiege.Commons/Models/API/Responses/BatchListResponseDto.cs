namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for the paginated batch list endpoint.
/// </summary>
public record BatchListResponseDto
{
    public IEnumerable<BatchListItemDto> Batches { get; init; } = null!;
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}
