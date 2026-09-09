namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for the country list endpoint.
/// </summary>
public record CountryListResponseDto
{
    public IEnumerable<CountryDto> Countries { get; init; } = null!;
}
