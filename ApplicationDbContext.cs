using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Comment> Comments => Set<Comment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Comment>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.AuthorId).IsRequired();
            entity.Property(c => c.ArticleId).IsRequired();
            entity.Property(c => c.Content).IsRequired().HasMaxLength(4000);
            entity.HasIndex(c => c.ArticleId);
            entity.HasIndex(c => new { c.ArticleId, c.CreatedAt });
            entity.HasIndex(c => new { c.AuthorId, c.CreatedAt });
        });
    }
}
