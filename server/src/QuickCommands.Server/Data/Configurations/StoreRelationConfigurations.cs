using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommands.Server.Entities;

namespace QuickCommands.Server.Data.Configurations;

internal sealed class StorePackageTagConfiguration : IEntityTypeConfiguration<StorePackageTag>
{
    public void Configure(EntityTypeBuilder<StorePackageTag> b)
    {
        b.ToTable("store_package_tags");
        b.HasKey(t => new { t.PackageId, t.Tag });
        b.Property(t => t.Tag).HasMaxLength(24);
        b.HasIndex(t => t.Tag);
    }
}

internal sealed class StoreStarConfiguration : IEntityTypeConfiguration<StoreStar>
{
    public void Configure(EntityTypeBuilder<StoreStar> b)
    {
        b.ToTable("store_stars");
        b.HasKey(s => new { s.PackageId, s.UserId });
        b.HasOne<StorePackage>().WithMany().HasForeignKey(s => s.PackageId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StoreInstallConfiguration : IEntityTypeConfiguration<StoreInstall>
{
    public void Configure(EntityTypeBuilder<StoreInstall> b)
    {
        b.ToTable("store_installs");
        b.HasKey(i => new { i.PackageId, i.UserId });
        b.HasOne<StorePackage>().WithMany().HasForeignKey(i => i.PackageId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StoreReportConfiguration : IEntityTypeConfiguration<StoreReport>
{
    public void Configure(EntityTypeBuilder<StoreReport> b)
    {
        b.ToTable("store_reports");
        b.HasKey(r => r.Id);
        b.Property(r => r.Reason).HasConversion<string>().HasMaxLength(16);
        b.Property(r => r.Resolution).HasConversion<string>().HasMaxLength(16);
        b.Property(r => r.Comment).HasMaxLength(1000);
        b.HasIndex(r => new { r.PackageId, r.ReporterId }).IsUnique();
        b.HasIndex(r => new { r.ResolvedAt, r.CreatedAt });
        b.HasOne(r => r.Package).WithMany().HasForeignKey(r => r.PackageId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(r => r.Reporter).WithMany().HasForeignKey(r => r.ReporterId).OnDelete(DeleteBehavior.Cascade);
    }
}
