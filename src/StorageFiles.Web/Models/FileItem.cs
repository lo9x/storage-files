using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StorageFiles.Web.Models;

[Table("Files")]
public class FileItem
{
    public int Id { get; set; }

    public int OwnerId { get; set; }
    public User? Owner { get; set; }

    /// <summary>Папка. null — корень личного пространства.</summary>
    public int? FolderId { get; set; }
    public Folder? Folder { get; set; }

    /// <summary>Имя, которое видит пользователь.</summary>
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Имя файла на диске (guid + расширение).</summary>
    [MaxLength(100)]
    public string StoredName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
