using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommands.Server.Entities;

namespace QuickCommands.Server.Data.Configurations;

internal sealed class CloudSaveRevisionConfiguration : IEntityTypeConfiguration<CloudSaveRevision>
{
    public void Configure(EntityTypeBuilder<CloudSaveRevision> b)
    {
        b.ToTable("cloud_save_revisions");
        b.HasKey(r => r.Id);
        b.HasIndex(r => new { r.UserId, r.Revision }).IsUnique();
        b.Property(r => r.Content).HasColumnType("mediumtext").IsRequired();
        b.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
