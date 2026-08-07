using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FutureKawaSiege.Data.Configurations;

public class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("Countries");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.Code)
            .IsRequired()
            .HasMaxLength(8);

        builder.HasIndex(c => c.Code)
            .IsUnique();

        builder.Property(c => c.NominalTemp)
            .IsRequired()
            .HasPrecision(5, 2);

        builder.Property(c => c.ToleranceTemp)
            .IsRequired()
            .HasPrecision(5, 2);

        builder.Property(c => c.NominalHumidity)
            .IsRequired()
            .HasPrecision(5, 2);

        builder.Property(c => c.ToleranceHumidity)
            .IsRequired()
            .HasPrecision(5, 2);
    }
}
