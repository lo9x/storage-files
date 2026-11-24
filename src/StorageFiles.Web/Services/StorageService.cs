using System.Globalization;
using Microsoft.Extensions.Options;

namespace StorageFiles.Web.Services;

/// <summary>
/// Работа с физическими файлами на диске. В БД хранится только имя и метаданные,
/// а на диске файл лежит под случайным именем в папке пользователя.
/// </summary>
public class StorageService
{
    private readonly string _rootPath;

    public StorageService(IOptions<StorageOptions> options, IWebHostEnvironment environment)
    {
        Options = options.Value;
        _rootPath = Path.IsPathRooted(Options.RootPath)
            ? Options.RootPath
            : Path.Combine(environment.ContentRootPath, Options.RootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public StorageOptions Options { get; }

    public long MaxFileSizeBytes => Options.MaxFileSizeMb * 1024L * 1024L;

    public string GetUserDirectory(int userId)
    {
        var directory = Path.Combine(_rootPath, userId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public string GetPhysicalPath(int userId, string storedName)
        => Path.Combine(GetUserDirectory(userId), storedName);

    public async Task<string> SaveAsync(int userId, IFormFile file, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(file.FileName);
        if (extension.Length > 12 || extension.Any(c => Path.GetInvalidFileNameChars().Contains(c)))
        {
            extension = string.Empty;
        }

        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = GetPhysicalPath(userId, storedName);

        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await file.CopyToAsync(stream, cancellationToken);
        return storedName;
    }

    public void DeletePhysical(int userId, string storedName)
    {
        try
        {
            var path = GetPhysicalPath(userId, storedName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Файл мог быть уже удалён или занят другим процессом — не ломаем пользовательский сценарий.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
