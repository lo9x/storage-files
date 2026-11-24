using System.ComponentModel.DataAnnotations;
using StorageFiles.Web.Models;

namespace StorageFiles.Web.ViewModels;

public class FilesIndexViewModel
{
    public int? FolderId { get; set; }
    public string? FolderName { get; set; }
    public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();
    public List<Folder> Folders { get; set; } = new();
    public List<FileItem> Files { get; set; } = new();
    public List<FolderOption> FolderOptions { get; set; } = new();

    public string? Search { get; set; }
    public string Sort { get; set; } = "name";

    public long UsedBytes { get; set; }
    public long LimitBytes { get; set; }
    public long AvailableBytes => Math.Max(0, LimitBytes - UsedBytes);
    public long MaxFileSizeBytes { get; set; }
    public int MaxFilesPerUpload { get; set; }

    public bool HasItems => Folders.Count > 0 || Files.Count > 0;
    public bool IsSearching => !string.IsNullOrWhiteSpace(Search);
}

public record BreadcrumbItem(int Id, string Name);

public record FolderOption(int Id, string Path);

public class TrashViewModel
{
    public List<Folder> Folders { get; set; } = new();
    public List<FileItem> Files { get; set; } = new();
    public bool IsEmpty => Folders.Count == 0 && Files.Count == 0;
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Введите имя")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Имя должно быть от 2 до 100 символов")]
    [Display(Name = "Как вас зовут")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите email")]
    [EmailAddress(ErrorMessage = "Некорректный email")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите пароль")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль должен быть не короче 6 символов")]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Повторите пароль")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают")]
    [Display(Name = "Повторите пароль")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class LoginViewModel
{
    [Required(ErrorMessage = "Введите email")]
    [EmailAddress(ErrorMessage = "Некорректный email")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите пароль")]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Запомнить меня")]
    public bool RememberMe { get; set; }
}

public class QuotaViewModel
{
    public long UsedBytes { get; set; }
    public long LimitBytes { get; set; }

    public int Percent => LimitBytes <= 0 ? 0 : (int)Math.Clamp(UsedBytes * 100 / LimitBytes, 0, 100);
}
