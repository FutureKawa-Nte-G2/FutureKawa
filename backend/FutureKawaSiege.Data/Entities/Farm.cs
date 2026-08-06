namespace FutureKawaSiege.Data.Entities;

/// <summary>
/// Represents a coffee farm located in a country.
/// </summary>
public class Farm
{
    public Guid Id { get; set; }
    public Guid CountryId { get; set; }
    public string Name { get; set; } = null!;
    public string Reference { get; set; } = null!;

    public Country Country { get; set; } = null!;
    public ICollection<Batch> Batches { get; set; } = new List<Batch>();
}
