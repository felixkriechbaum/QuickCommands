using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommands.Server.Entities;

namespace QuickCommands.Server.Data.Configurations;

internal sealed class StoreCommandConfiguration : IEntityTypeConfiguration<StoreCommand>
{
    public void Configure(EntityTypeBuilder<StoreCommand> b)
    {
        b.ToTable("store_commands");
        b.HasKey(c => c.Id);
        b.Property(c => c.Name).HasMaxLength(64).IsRequired();
        b.Property(c => c.Language).HasConversion<string>().HasMaxLength(16);
        b.Property(c => c.Description).HasMaxLength(500).IsRequired();
        b.Property(c => c.Script).HasColumnType("mediumtext").IsRequired();
        b.Property(c => c.ParametersJson).HasColumnType("mediumtext");
        b.HasIndex(c => new { c.PackageId, c.SortOrder });
        b.HasIndex(c => c.Name);
    }
}
