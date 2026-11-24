using System.ComponentModel.DataAnnotations;

namespace StorageFiles.Web.Models;

public class User
{
    public int Id { get; set; }

    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Role { get; set; } = RoleNames.User;

    /// <summary>Лимит хранения в байтах.</summary>
    public long StorageLimitBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Folder> Folders { get; set; } = new();
    public List<FileItem> Files { get; set; } = new();
}
