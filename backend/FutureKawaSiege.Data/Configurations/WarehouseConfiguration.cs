using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FutureKawaSiege.Data.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(w => w.Reference)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(w => w.Reference)
            .IsUnique();

        builder.HasOne(w => w.Country)
            .WithMany(c => c.Warehouses)
            .HasForeignKey(w => w.CountryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
