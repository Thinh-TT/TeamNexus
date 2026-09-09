using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class BoardColumnConfiguration : IEntityTypeConfiguration<BoardColumn>
{
    public void Configure(EntityTypeBuilder<BoardColumn> builder)
    {
        builder.ToTable("board_columns");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(c => c.IsDone)
            .HasDefaultValue(false);

        // Column order must be unique within a board (DB design §5).
        builder.HasIndex(c => new { c.BoardId, c.Position })
            .IsUnique();

        builder.HasOne(c => c.Board)
            .WithMany()
            .HasForeignKey(c => c.BoardId)
            .OnDelete(DeleteBehavior.Restrict);

        // A column is only visible while its board is not deleted. Board has no
        // physical cascade (DB design §7), so soft-deleting a board hides all its columns.
        builder.HasQueryFilter(c => c.Board == null || c.Board.DeletedAt == null);
    }
}
