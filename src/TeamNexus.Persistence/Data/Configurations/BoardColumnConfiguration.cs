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

        builder.Property(c => c.IsClarification)
            .HasDefaultValue(false);

        builder.Property(c => c.IsInReview)
            .HasDefaultValue(false);

        // Column order must be unique within a board (DB design §5).
        builder.HasIndex(c => new { c.BoardId, c.Position })
            .IsUnique();

        // At most ONE "Chờ làm rõ" column per board (Phase 7 §2.2 / DB design §5). Enforced by a
        // partial unique index, which is also the race guard when the Ai module lazily creates the
        // column for the first time.
        builder.HasIndex(c => c.BoardId)
            .IsUnique()
            .HasFilter("\"is_clarification\"")
            .HasDatabaseName("uq_board_columns_clarification");

        // At most ONE "Đang duyệt PR" (In Review) column per board (Phase 15.1 §T15.1.1).
        // Pass an explicit model index name so EF Core does not overwrite the existing
        // unnamed index on BoardId (uq_board_columns_clarification).
        builder.HasIndex(c => c.BoardId, "IX_board_columns_board_id_single_in_review")
            .IsUnique()
            .HasFilter("is_in_review = true")
            .HasDatabaseName("IX_board_columns_board_id_single_in_review");

        builder.HasOne(c => c.Board)
            .WithMany()
            .HasForeignKey(c => c.BoardId)
            .OnDelete(DeleteBehavior.Restrict);

        // A column is only visible while its board is not deleted. Board has no
        // physical cascade (DB design §7), so soft-deleting a board hides all its columns.
        builder.HasQueryFilter(c => c.Board == null || c.Board.DeletedAt == null);
    }
}
