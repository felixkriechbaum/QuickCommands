using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommands.Server.Entities;

namespace QuickCommands.Server.Data.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);
        b.Property(u => u.FirebaseUid).HasMaxLength(128).IsRequired();
        b.HasIndex(u => u.FirebaseUid).IsUnique();
        b.Property(u => u.Handle).HasMaxLength(20);
        b.HasIndex(u => u.Handle).IsUnique();
        b.Property(u => u.BanReason).HasMaxLength(500);
    }
}
