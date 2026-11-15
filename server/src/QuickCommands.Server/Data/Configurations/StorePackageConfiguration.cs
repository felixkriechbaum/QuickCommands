using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommands.Server.Entities;

namespace QuickCommands.Server.Data.Configurations;

internal sealed class StorePackageConfiguration : IEntityTypeConfiguration<StorePackage>
{
    public void Configure(EntityTypeBuilder<StorePackage> b)
    {
        b.ToTable("store_packages");
        b.HasKey(p => p.Id);
        b.Property(p => p.Title).HasMaxLength(80).IsRequired();
        b.Property(p => p.Description).HasMaxLength(2000).IsRequired();
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(p => p.RemovalReason).HasConversion<string>().HasMaxLength(16);

        b.HasOne(p => p.Owner).WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(p => p.Commands).WithOne().HasForeignKey(c => c.PackageId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(p => p.Tags).WithOne().HasForeignKey(t => t.PackageId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(p => new { p.Status, p.CreatedAt });
        b.HasIndex(p => new { p.Status, p.StarCount });
        b.HasIndex(p => new { p.Status, p.InstallCount });
        b.HasIndex(p => p.OwnerId);
    }
}
