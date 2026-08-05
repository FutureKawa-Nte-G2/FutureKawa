namespace FutureKawaSiege.Data.Entities;

/// <summary>
/// Represents a country with climate reference values for coffee storage.
/// </summary>
public class Country
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public decimal NominalTemp { get; set; }
    public decimal ToleranceTemp { get; set; }
    public decimal NominalHumidity { get; set; }
    public decimal ToleranceHumidity { get; set; }

    public ICollection<Farm> Farms { get; set; } = new List<Farm>();
    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
}
