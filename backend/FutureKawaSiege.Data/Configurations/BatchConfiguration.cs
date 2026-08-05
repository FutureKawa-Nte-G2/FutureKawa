using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FutureKawaSiege.Data.Configurations;

public class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("Batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .UseIdentityByDefaultColumn();

        builder.Property(b => b.Reference)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(b => b.Reference)
            .IsUnique();

        builder.Property(b => b.StoredAt)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(b => b.ShippedAt)
            .HasColumnType("date");

        builder.Property(b => b.QualityGrade)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(8);

        builder.Property(b => b.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(16);

        builder.HasOne(b => b.Warehouse)
            .WithMany(w => w.Batches)
            .HasForeignKey(b => b.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Farm)
            .WithMany(f => f.Batches)
            .HasForeignKey(b => b.FarmId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
