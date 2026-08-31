using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FutureKawaSiege.Data.Configurations;

public class BatchAlertConfiguration : IEntityTypeConfiguration<BatchAlert>
{
    public void Configure(EntityTypeBuilder<BatchAlert> builder)
    {
        builder.ToTable("BatchAlerts");

        builder.HasKey(ba => ba.Id);

        builder.Property(ba => ba.Type)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(ba => ba.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(ba => ba.CreatedAt)
            .IsRequired();

        builder.Property(ba => ba.ResolvedAt);

        builder.Property(ba => ba.MeasuredAt);

        builder.HasOne(ba => ba.Warehouse)
            .WithMany(w => w.Alerts)
            .HasForeignKey(ba => ba.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ba => ba.Batch)
            .WithMany(b => b.Alerts)
            .HasForeignKey(ba => ba.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ba => new { ba.BatchId, ba.Type, ba.Status });
    }
}
