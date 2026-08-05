namespace FutureKawaSiege.Data.Entities;

/// <summary>
/// Represents a warehouse located in a country where coffee batches are stored.
/// </summary>
public class Warehouse
{
    public int Id { get; set; }
    public int CountryId { get; set; }
    public string Name { get; set; } = null!;
    public string Reference { get; set; } = null!;

    public Country Country { get; set; } = null!;
    public ICollection<Batch> Batches { get; set; } = new List<Batch>();
    public ICollection<User> Users { get; set; } = new List<User>();
}
