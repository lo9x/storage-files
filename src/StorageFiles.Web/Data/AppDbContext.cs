using Microsoft.EntityFrameworkCore;
using StorageFiles.Web.Models;

namespace StorageFiles.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FileItem> Files => Set<FileItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Folder>(entity =>
        {
            entity.HasOne(f => f.Owner)
                .WithMany(u => u.Folders)
                .HasForeignKey(f => f.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(f => f.Parent)
                .WithMany(f => f.Children)
                .HasForeignKey(f => f.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(f => new { f.OwnerId, f.ParentId });
            entity.HasIndex(f => new { f.OwnerId, f.IsDeleted });
        });

        modelBuilder.Entity<FileItem>(entity =>
        {
            entity.HasOne(f => f.Owner)
                .WithMany(u => u.Files)
                .HasForeignKey(f => f.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(f => f.Folder)
                .WithMany(f => f.Files)
                .HasForeignKey(f => f.FolderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(f => new { f.OwnerId, f.FolderId });
            entity.HasIndex(f => new { f.OwnerId, f.IsDeleted });
        });
    }
}
