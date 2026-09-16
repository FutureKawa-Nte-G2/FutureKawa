using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FutureKawaSiege.Data.Configurations;

public class FarmConfiguration : IEntityTypeConfiguration<Farm>
{
    public void Configure(EntityTypeBuilder<Farm> builder)
    {
        builder.ToTable("Farms");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(f => f.Reference)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(f => f.Reference)
            .IsUnique();

        builder.HasOne(f => f.Country)
            .WithMany(c => c.Farms)
            .HasForeignKey(f => f.CountryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
