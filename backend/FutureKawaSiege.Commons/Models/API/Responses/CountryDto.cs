namespace FutureKawaSiege.Commons.Models.API.Responses;

/// <summary>
/// Response DTO for a country.
/// </summary>
public record CountryDto
{
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
}
