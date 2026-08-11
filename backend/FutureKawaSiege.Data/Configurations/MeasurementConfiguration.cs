using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FutureKawaSiege.Data.Configurations;

public class MeasurementConfiguration : IEntityTypeConfiguration<Measurement>
{
    public void Configure(EntityTypeBuilder<Measurement> builder)
    {
        builder.ToTable("Measurements");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.AvgMeasTemp)
            .HasPrecision(5, 2);

        builder.Property(m => m.MaxMeasTemp)
            .HasPrecision(5, 2);

        builder.Property(m => m.MinMeasTemp)
            .HasPrecision(5, 2);

        builder.Property(m => m.AvgMeasHumidity)
            .HasPrecision(5, 2);

        builder.Property(m => m.MinMeasHumidity)
            .HasPrecision(5, 2);

        builder.Property(m => m.MaxMeasHumidity)
            .HasPrecision(5, 2);

        builder.Property(m => m.MeasDate)
            .HasColumnType("timestamptz");

        builder.HasOne(m => m.Warehouse)
            .WithMany(w => w.Measurements)
            .HasForeignKey(m => m.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.WarehouseId, m.MeasDate })
            .IsUnique();
    }
}