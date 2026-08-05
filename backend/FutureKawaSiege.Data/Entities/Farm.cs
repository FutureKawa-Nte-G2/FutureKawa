namespace FutureKawaSiege.Data.Entities;

/// <summary>
/// Represents a coffee farm located in a country.
/// </summary>
public class Farm
{
    public int Id { get; set; }
    public int CountryId { get; set; }
    public string Name { get; set; } = null!;
    public string Reference { get; set; } = null!;

    public Country Country { get; set; } = null!;
    public ICollection<Batch> Batches { get; set; } = new List<Batch>();
}
