using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        // Identity table names are mapped to snake_case explicitly (DB design §3.1).
        builder.ToTable("users");

        builder.Property(u => u.DisplayName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(u => u.AvatarUrl)
            .HasMaxLength(2048);

        // Daily work digest opt-out (Phase 13 §3.1). The database default is what makes the
        // migration additive: existing rows receive `true` without a backfill statement, so
        // nobody's behaviour changes the day the column appears.
        builder.Property(u => u.DigestEnabled)
            .HasDefaultValue(true)
            .IsRequired();
    }
}
