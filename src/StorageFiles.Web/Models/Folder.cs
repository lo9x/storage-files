using System.ComponentModel.DataAnnotations;

namespace StorageFiles.Web.Models;

public class Folder
{
    public int Id { get; set; }

    public int OwnerId { get; set; }
    public User? Owner { get; set; }

    /// <summary>Родительская папка. null — корень личного пространства.</summary>
    public int? ParentId { get; set; }
    public Folder? Parent { get; set; }

    public List<Folder> Children { get; set; } = new();
    public List<FileItem> Files { get; set; } = new();

    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
