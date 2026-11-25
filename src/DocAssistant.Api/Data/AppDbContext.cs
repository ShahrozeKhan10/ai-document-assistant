using DocAssistant.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.PasswordHash).IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Document>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.FileName).HasMaxLength(260).IsRequired();
            e.Property(d => d.StoredFileName).HasMaxLength(100).IsRequired();
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.Error).HasMaxLength(2000);

            // Every query filters by owner, so OwnerId is indexed (EF adds the index for the foreign key).
            e.HasOne(d => d.Owner)
             .WithMany()
             .HasForeignKey(d => d.OwnerId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(d => d.Chunks)
             .WithOne(c => c.Document)
             .HasForeignKey(c => c.DocumentId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentChunk>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Content).IsRequired();
            e.HasIndex(c => new { c.DocumentId, c.Index }).IsUnique();
        });
    }
}
