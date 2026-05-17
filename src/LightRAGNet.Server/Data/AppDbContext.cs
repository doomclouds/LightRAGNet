using Microsoft.EntityFrameworkCore;
using LightRAGNet.Server.Models;

namespace LightRAGNet.Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<MarkdownDocument> MarkdownDocuments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MarkdownDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.FileSize).IsRequired();
            entity.Property(e => e.UploadTime).IsRequired();
            entity.Property(e => e.IsInRagSystem).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.RagStatus).HasMaxLength(50);
            entity.Property(e => e.RagProgress).IsRequired().HasDefaultValue(0);
            entity.Property(e => e.RagDocumentId).HasMaxLength(200);
            entity.Property(e => e.RagErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.FileUrl).HasMaxLength(500);
            entity.Property(e => e.FileHash).HasMaxLength(64);
            entity.HasIndex(e => e.FileName);
            entity.HasIndex(e => e.IsInRagSystem);
            entity.HasIndex(e => e.RagDocumentId);
            entity.HasIndex(e => e.FileHash);
        });
    }
}
