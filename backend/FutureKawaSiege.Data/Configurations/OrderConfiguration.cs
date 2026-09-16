using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FutureKawaSiege.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.OdooOrderId);

        builder.Property(o => o.OrderReference)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(o => o.OdooOrderId);

        builder.Property(o => o.OrderDate)
            .IsRequired();

        builder.Property(o => o.ClientName)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasOne(o => o.Country)
            .WithMany()
            .HasForeignKey(o => o.CountryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(o => o.IntegrationErrorMessage)
            .HasMaxLength(1024);

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.Property(o => o.UpdatedAt);

        builder.HasMany(o => o.Lines)
            .WithOne(l => l.Order)
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.Batches)
            .WithMany(b => b.Orders)
            .UsingEntity<Dictionary<string, object>>(
                "OrderBatches",
                j => j.HasOne<Batch>().WithMany().HasForeignKey("BatchId"),
                j => j.HasOne<Order>().WithMany().HasForeignKey("OrderId"),
                j =>
                {
                    j.Property<Guid>("BatchId");
                    j.Property<Guid>("OrderId");
                });
    }
}