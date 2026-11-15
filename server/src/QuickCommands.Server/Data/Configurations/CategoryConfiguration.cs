using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickCommands.Server.Entities;

namespace QuickCommands.Server.Data.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    /// <summary>The fixed category list. New categories are added here and shipped with a migration.</summary>
    internal static readonly Category[] Seed =
    [
        new() { Id = 1, Slug = "git", NameDe = "Git", NameEn = "Git", SortOrder = 10 },
        new() { Id = 2, Slug = "navigation", NameDe = "Navigation", NameEn = "Navigation", SortOrder = 20 },
        new() { Id = 3, Slug = "files", NameDe = "Dateien", NameEn = "Files", SortOrder = 30 },
        new() { Id = 4, Slug = "docker", NameDe = "Docker", NameEn = "Docker", SortOrder = 40 },
        new() { Id = 5, Slug = "build", NameDe = "Build", NameEn = "Build", SortOrder = 50 },
        new() { Id = 6, Slug = "dev-tools", NameDe = "Entwicklung", NameEn = "Dev tools", SortOrder = 60 },
        new() { Id = 7, Slug = "network", NameDe = "Netzwerk", NameEn = "Network", SortOrder = 70 },
        new() { Id = 8, Slug = "system", NameDe = "System", NameEn = "System", SortOrder = 80 },
        new() { Id = 9, Slug = "other", NameDe = "Sonstiges", NameEn = "Other", SortOrder = 1000 },
    ];

    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Slug).HasMaxLength(32).IsRequired();
        b.HasIndex(c => c.Slug).IsUnique();
        b.Property(c => c.NameDe).HasMaxLength(64).IsRequired();
        b.Property(c => c.NameEn).HasMaxLength(64).IsRequired();
        b.HasData(Seed);
    }
}
