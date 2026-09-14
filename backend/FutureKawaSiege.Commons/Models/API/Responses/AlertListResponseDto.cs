namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for the paginated alert list endpoint (#85).
/// </summary>
public record AlertListResponseDto
{
    public IEnumerable<AlertListItemDto> Alerts { get; init; } = null!;
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}
