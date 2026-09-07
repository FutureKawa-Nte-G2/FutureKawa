using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FutureKawaSiege.Data.Configurations;

public class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("Alerts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Type)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(a => a.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        builder.Property(a => a.ResolvedAt);

        builder.Property(a => a.MeasuredAt);

        builder.HasOne(a => a.Warehouse)
            .WithMany(w => w.Alerts)
            .HasForeignKey(a => a.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.WarehouseId, a.Type, a.Status });
    }
}
