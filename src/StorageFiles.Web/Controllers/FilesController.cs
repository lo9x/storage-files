using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StorageFiles.Web.Data;
using StorageFiles.Web.Helpers;
using StorageFiles.Web.Models;
using StorageFiles.Web.Services;
using StorageFiles.Web.ViewModels;

namespace StorageFiles.Web.Controllers;

[Authorize]
public class FilesController : Controller
{
    private readonly AppDbContext _db;
    private readonly StorageService _storage;

    public FilesController(AppDbContext db, StorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? folderId, string? search, string? sort, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();

        Folder? currentFolder = null;
        if (folderId.HasValue)
        {
            currentFolder = await _db.Folders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == folderId.Value && f.OwnerId == userId && !f.IsDeleted, cancellationToken);

            if (currentFolder is null)
            {
                return NotFound();
            }
        }

        var folders = await _db.Folders
            .AsNoTracking()
            .Where(f => f.OwnerId == userId && f.ParentId == folderId && !f.IsDeleted)
            .ToListAsync(cancellationToken);

        var files = await _db.Files
            .AsNoTracking()
            .Where(f => f.OwnerId == userId && f.FolderId == folderId && !f.IsDeleted)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            folders = folders.Where(f => f.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
            files = files.Where(f => f.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var (sortField, descending) = ParseSort(sort);
        SortItems(folders, f => f.Name, _ => 0, f => f.UpdatedAt, sortField, descending);
        SortItems(files, f => f.Name, f => f.SizeBytes, f => f.UpdatedAt, sortField, descending);

        var (usedBytes, limitBytes) = await GetQuotaAsync(userId, cancellationToken);

        var model = new FilesIndexViewModel
        {
            FolderId = folderId,
            FolderName = currentFolder?.Name,
            Breadcrumbs = await BuildBreadcrumbsAsync(userId, currentFolder, cancellationToken),
            Folders = folders,
            Files = files,
            FolderOptions = await GetFolderOptionsAsync(userId, cancellationToken),
            Search = search?.Trim(),
            Sort = sortField + (descending ? "_desc" : string.Empty),
            UsedBytes = usedBytes,
            LimitBytes = limitBytes,
            MaxFileSizeBytes = _storage.MaxFileSizeBytes,
            MaxFilesPerUpload = _storage.Options.MaxFilesPerUpload
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(List<IFormFile>? files, int? folderId, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();

        if (folderId.HasValue && !await FolderExistsAsync(userId, folderId.Value, cancellationToken))
        {
            return NotFound();
        }

        var uploads = files?.Where(f => f.Length > 0).ToList() ?? new List<IFormFile>();
        if (uploads.Count == 0)
        {
            TempData["Error"] = "Выберите хотя бы один файл.";
            return RedirectToFolder(folderId);
        }

        if (uploads.Count > _storage.Options.MaxFilesPerUpload)
        {
            TempData["Error"] = $"За один раз можно загрузить не больше {_storage.Options.MaxFilesPerUpload} файлов.";
            return RedirectToFolder(folderId);
        }

        var tooLarge = uploads.FirstOrDefault(f => f.Length > _storage.MaxFileSizeBytes);
        if (tooLarge is not null)
        {
            TempData["Error"] = $"Файл «{tooLarge.FileName}» больше {Format.Bytes(_storage.MaxFileSizeBytes)}.";
            return RedirectToFolder(folderId);
        }

        var (usedBytes, limitBytes) = await GetQuotaAsync(userId, cancellationToken);
        var totalBytes = uploads.Sum(f => f.Length);
        if (usedBytes + totalBytes > limitBytes)
        {
            var available = Math.Max(0, limitBytes - usedBytes);
            TempData["Error"] = $"Недостаточно места. Свободно {Format.Bytes(available)}, а нужно {Format.Bytes(totalBytes)}.";
            return RedirectToFolder(folderId);
        }

        var savedOnDisk = new List<string>();
        try
        {
            foreach (var upload in uploads)
            {
                var name = await MakeUniqueFileNameAsync(userId, folderId, MakeSafeFileName(upload.FileName), cancellationToken);
                var storedName = await _storage.SaveAsync(userId, upload, cancellationToken);
                savedOnDisk.Add(storedName);

                _db.Files.Add(new FileItem
                {
                    OwnerId = userId,
                    FolderId = folderId,
                    Name = name,
                    StoredName = storedName,
                    ContentType = string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType,
                    SizeBytes = upload.Length
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            foreach (var storedName in savedOnDisk)
            {
                _storage.DeletePhysical(userId, storedName);
            }

            throw;
        }

        TempData["Success"] = $"Загружено файлов: {uploads.Count} ({Format.Bytes(totalBytes)}).";
        return RedirectToFolder(folderId);
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();
        var file = await _db.Files
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted, cancellationToken);

        if (file is null)
        {
            return NotFound();
        }

        var path = _storage.GetPhysicalPath(userId, file.StoredName);
        if (!System.IO.File.Exists(path))
        {
            TempData["Error"] = "Файл не найден на диске.";
            return RedirectToFolder(file.FolderId);
        }

        return PhysicalFile(path, file.ContentType, file.Name, enableRangeProcessing: true);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFolder(string? name, int? parentId, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();

        if (parentId.HasValue && !await FolderExistsAsync(userId, parentId.Value, cancellationToken))
        {
            return NotFound();
        }

        var cleanName = CleanName(name);
        if (cleanName is null)
        {
            TempData["Error"] = "Введите корректное имя папки (до 255 символов).";
            return RedirectToFolder(parentId);
        }

        if (await FolderNameExistsAsync(userId, parentId, cleanName, excludeId: null, cancellationToken))
        {
            TempData["Error"] = $"Папка «{cleanName}» уже существует.";
            return RedirectToFolder(parentId);
        }

        _db.Folders.Add(new Folder
        {
            OwnerId = userId,
            ParentId = parentId,
            Name = cleanName
        });
        await _db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = $"Папка «{cleanName}» создана.";
        return RedirectToFolder(parentId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameFolder(int id, string? name, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted, cancellationToken);

        if (folder is null)
        {
            return NotFound();
        }

        var cleanName = CleanName(name);
        if (cleanName is null)
        {
            TempData["Error"] = "Введите корректное имя папки.";
            return RedirectToFolder(folder.ParentId);
        }

        if (await FolderNameExistsAsync(userId, folder.ParentId, cleanName, folder.Id, cancellationToken))
        {
            TempData["Error"] = $"Папка «{cleanName}» уже существует.";
            return RedirectToFolder(folder.ParentId);
        }

        folder.Name = cleanName;
        folder.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Папка переименована.";
        return RedirectToFolder(folder.ParentId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFolder(int id, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted, cancellationToken);

        if (folder is null)
        {
            return NotFound();
        }

        var parentId = folder.ParentId;
        var subtree = await GetSubtreeAsync(userId, folder.Id, cancellationToken);
        var folderIds = subtree.Select(f => f.Id).ToHashSet();
        var now = DateTime.UtcNow;

        foreach (var item in subtree)
        {
            item.IsDeleted = true;
            item.DeletedAt = now;
        }

        var files = await _db.Files
            .Where(f => f.OwnerId == userId && f.FolderId != null && folderIds.Contains(f.FolderId.Value) && !f.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var file in files)
        {
            file.IsDeleted = true;
            file.DeletedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = $"Папка «{folder.Name}» перемещена в корзину.";
        return RedirectToFolder(parentId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameFile(int id, string? name, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();
        var file = await _db.Files
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted, cancellationToken);

        if (file is null)
        {
            return NotFound();
        }

        var cleanName = CleanName(name);
        if (cleanName is null)
        {
            TempData["Error"] = "Введите корректное имя файла.";
            return RedirectToFolder(file.FolderId);
        }

        if (await FileNameExistsAsync(userId, file.FolderId, cleanName, file.Id, cancellationToken))
        {
            TempData["Error"] = $"Файл «{cleanName}» уже существует в этой папке.";
            return RedirectToFolder(file.FolderId);
        }

        file.Name = cleanName;
        file.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Файл переименован.";
        return RedirectToFolder(file.FolderId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveFile(int id, int? targetFolderId, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();

        if (targetFolderId.HasValue && !await FolderExistsAsync(userId, targetFolderId.Value, cancellationToken))
        {
            return NotFound();
        }

        var file = await _db.Files
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted, cancellationToken);

        if (file is null)
        {
            return NotFound();
        }

        if (file.FolderId == targetFolderId)
        {
            TempData["Error"] = "Файл уже находится в этой папке.";
            return RedirectToFolder(file.FolderId);
        }

        file.Name = await MakeUniqueFileNameAsync(userId, targetFolderId, file.Name, cancellationToken);
        file.FolderId = targetFolderId;
        file.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = $"Файл «{file.Name}» перемещён.";
        return RedirectToFolder(targetFolderId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveFolder(int id, int? targetFolderId, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted, cancellationToken);

        if (folder is null)
        {
            return NotFound();
        }

        if (folder.ParentId == targetFolderId)
        {
            TempData["Error"] = "Папка уже находится в этом месте.";
            return RedirectToFolder(folder.ParentId);
        }

        if (targetFolderId.HasValue)
        {
            if (!await FolderExistsAsync(userId, targetFolderId.Value, cancellationToken))
            {
                return NotFound();
            }

            var subtree = await GetSubtreeAsync(userId, folder.Id, cancellationToken);
            if (subtree.Any(f => f.Id == targetFolderId.Value))
            {
                TempData["Error"] = "Нельзя переместить папку внутрь себя.";
                return RedirectToFolder(folder.ParentId);
            }
        }

        var cleanName = folder.Name;
        if (await FolderNameExistsAsync(userId, targetFolderId, cleanName, folder.Id, cancellationToken))
        {
            TempData["Error"] = $"Папка «{cleanName}» уже существует в выбранном месте.";
            return RedirectToFolder(folder.ParentId);
        }

        folder.ParentId = targetFolderId;
        folder.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = $"Папка «{folder.Name}» перемещена.";
        return RedirectToFolder(targetFolderId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFile(int id, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();
        var file = await _db.Files
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && !f.IsDeleted, cancellationToken);

        if (file is null)
        {
            return NotFound();
        }

        file.IsDeleted = true;
        file.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = $"Файл «{file.Name}» перемещён в корзину.";
        return RedirectToFolder(file.FolderId);
    }

    [HttpGet]
    public async Task<IActionResult> Trash(CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();

        var folders = await _db.Folders
            .AsNoTracking()
            .Where(f => f.OwnerId == userId && f.IsDeleted)
            .ToListAsync(cancellationToken);

        var files = await _db.Files
            .AsNoTracking()
            .Where(f => f.OwnerId == userId && f.IsDeleted)
            .ToListAsync(cancellationToken);

        // Показываем только верхний уровень удалённого, чтобы не дублировать содержимое папок.
        var deletedFolderIds = folders.Select(f => f.Id).ToHashSet();
        folders = folders
            .Where(f => f.ParentId is null || !deletedFolderIds.Contains(f.ParentId.Value))
            .OrderByDescending(f => f.DeletedAt)
            .ToList();

        files = files
            .Where(f => f.FolderId is null || !deletedFolderIds.Contains(f.FolderId.Value))
            .OrderByDescending(f => f.DeletedAt)
            .ToList();

        return View(new TrashViewModel { Folders = folders, Files = files });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreFile(int id, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();
        var file = await _db.Files
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && f.IsDeleted, cancellationToken);

        if (file is null)
        {
            return NotFound();
        }

        var (usedBytes, limitBytes) = await GetQuotaAsync(userId, cancellationToken);
        if (usedBytes + file.SizeBytes > limitBytes)
        {
            TempData["Error"] = "Недостаточно места для восстановления файла.";
            return RedirectToAction(nameof(Trash));
        }

        if (file.FolderId.HasValue)
        {
            var parent = await _db.Folders
                .FirstOrDefaultAsync(f => f.Id == file.FolderId && f.OwnerId == userId, cancellationToken);
            if (parent is null || parent.IsDeleted)
            {
                file.FolderId = null;
            }
        }

        file.IsDeleted = false;
        file.DeletedAt = null;
        file.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = $"Файл «{file.Name}» восстановлен.";
        return RedirectToAction(nameof(Trash));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreFolder(int id, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && f.IsDeleted, cancellationToken);

        if (folder is null)
        {
            return NotFound();
        }

        var subtree = await GetSubtreeAsync(userId, folder.Id, cancellationToken);
        var folderIds = subtree.Select(f => f.Id).ToHashSet();

        var files = await _db.Files
            .Where(f => f.OwnerId == userId && f.FolderId != null && folderIds.Contains(f.FolderId.Value) && f.IsDeleted)
            .ToListAsync(cancellationToken);

        var (usedBytes, limitBytes) = await GetQuotaAsync(userId, cancellationToken);
        var restoreBytes = files.Sum(f => f.SizeBytes);
        if (usedBytes + restoreBytes > limitBytes)
        {
            TempData["Error"] = "Недостаточно места для восстановления папки.";
            return RedirectToAction(nameof(Trash));
        }

        var now = DateTime.UtcNow;
        foreach (var item in subtree)
        {
            item.IsDeleted = false;
            item.DeletedAt = null;
            item.UpdatedAt = now;
        }

        foreach (var file in files)
        {
            file.IsDeleted = false;
            file.DeletedAt = null;
            file.UpdatedAt = now;
        }

        if (folder.ParentId.HasValue)
        {
            var parent = await _db.Folders
                .FirstOrDefaultAsync(f => f.Id == folder.ParentId && f.OwnerId == userId, cancellationToken);
            if (parent is null || parent.IsDeleted)
            {
                folder.ParentId = null;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = $"Папка «{folder.Name}» восстановлена.";
        return RedirectToAction(nameof(Trash));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PurgeFile(int id, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();
        var file = await _db.Files
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && f.IsDeleted, cancellationToken);

        if (file is null)
        {
            return NotFound();
        }

        _storage.DeletePhysical(userId, file.StoredName);
        _db.Files.Remove(file);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = $"Файл «{file.Name}» удалён навсегда.";
        return RedirectToAction(nameof(Trash));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PurgeFolder(int id, CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == userId && f.IsDeleted, cancellationToken);

        if (folder is null)
        {
            return NotFound();
        }

        var subtree = await GetSubtreeAsync(userId, folder.Id, cancellationToken);
        var folderIds = subtree.Select(f => f.Id).ToHashSet();

        var files = await _db.Files
            .Where(f => f.OwnerId == userId && f.FolderId != null && folderIds.Contains(f.FolderId.Value))
            .ToListAsync(cancellationToken);

        foreach (var file in files)
        {
            _storage.DeletePhysical(userId, file.StoredName);
            _db.Files.Remove(file);
        }

        RemoveFoldersDeepestFirst(subtree);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = $"Папка «{folder.Name}» удалена навсегда.";
        return RedirectToAction(nameof(Trash));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmptyTrash(CancellationToken cancellationToken = default)
    {
        var userId = User.GetUserId();

        var files = await _db.Files
            .Where(f => f.OwnerId == userId && f.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var file in files)
        {
            _storage.DeletePhysical(userId, file.StoredName);
            _db.Files.Remove(file);
        }

        var folders = await _db.Folders
            .Where(f => f.OwnerId == userId && f.IsDeleted)
            .ToListAsync(cancellationToken);

        RemoveFoldersDeepestFirst(folders);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Корзина очищена.";
        return RedirectToAction(nameof(Trash));
    }

    private static (string Field, bool Descending) ParseSort(string? sort)
    {
        var value = (sort ?? "name").Trim().ToLowerInvariant();
        var descending = value.EndsWith("_desc", StringComparison.Ordinal);
        var field = descending ? value[..^5] : value;

        if (field is not ("name" or "size" or "date"))
        {
            field = "name";
            descending = false;
        }

        return (field, descending);
    }

    private static void SortItems<T>(
        List<T> items,
        Func<T, string> name,
        Func<T, long> size,
        Func<T, DateTime> date,
        string field,
        bool descending)
    {
        Comparison<T> comparison = field switch
        {
            "size" => (a, b) => size(a).CompareTo(size(b)),
            "date" => (a, b) => date(a).CompareTo(date(b)),
            _ => (a, b) => string.Compare(name(a), name(b), StringComparison.CurrentCultureIgnoreCase)
        };

        items.Sort((a, b) => descending ? comparison(b, a) : comparison(a, b));
    }

    private async Task<(long Used, long Limit)> GetQuotaAsync(int userId, CancellationToken cancellationToken)
    {
        var used = await _db.Files
            .Where(f => f.OwnerId == userId && !f.IsDeleted)
            .SumAsync(f => (long?)f.SizeBytes, cancellationToken) ?? 0;

        var limit = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.StorageLimitBytes)
            .FirstOrDefaultAsync(cancellationToken);

        return (used, limit);
    }

    private async Task<List<BreadcrumbItem>> BuildBreadcrumbsAsync(int userId, Folder? folder, CancellationToken cancellationToken)
    {
        var breadcrumbs = new List<BreadcrumbItem>();
        var guard = 0;

        while (folder is not null && guard++ < 100)
        {
            breadcrumbs.Insert(0, new BreadcrumbItem(folder.Id, folder.Name));

            if (folder.ParentId is not int parentId)
            {
                break;
            }

            folder = await _db.Folders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == parentId && f.OwnerId == userId, cancellationToken);
        }

        return breadcrumbs;
    }

    private async Task<List<FolderOption>> GetFolderOptionsAsync(int userId, CancellationToken cancellationToken)
    {
        var folders = await _db.Folders
            .AsNoTracking()
            .Where(f => f.OwnerId == userId && !f.IsDeleted)
            .ToListAsync(cancellationToken);

        var byId = folders.ToDictionary(f => f.Id);

        string BuildPath(Folder folder)
        {
            var parts = new List<string>();
            Folder? current = folder;
            var guard = 0;

            while (current is not null && guard++ < 100)
            {
                parts.Insert(0, current.Name);
                current = current.ParentId is int parentId && byId.TryGetValue(parentId, out var parent)
                    ? parent
                    : null;
            }

            return string.Join(" / ", parts);
        }

        return folders
            .OrderBy(BuildPath, StringComparer.CurrentCultureIgnoreCase)
            .Select(f => new FolderOption(f.Id, BuildPath(f)))
            .ToList();
    }

    private async Task<List<Folder>> GetSubtreeAsync(int userId, int rootId, CancellationToken cancellationToken)
    {
        var folders = await _db.Folders
            .Where(f => f.OwnerId == userId)
            .ToListAsync(cancellationToken);

        var childrenByParent = folders.ToLookup(f => f.ParentId);
        var visited = new HashSet<int>();
        var result = new List<Folder>();
        var queue = new Queue<int>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!visited.Add(id))
            {
                continue;
            }

            var folder = folders.FirstOrDefault(f => f.Id == id);
            if (folder is null)
            {
                continue;
            }

            result.Add(folder);
            foreach (var child in childrenByParent[id])
            {
                queue.Enqueue(child.Id);
            }
        }

        return result;
    }

    private void RemoveFoldersDeepestFirst(List<Folder> folders)
    {
        var byId = folders.ToDictionary(f => f.Id);

        int Depth(Folder folder)
        {
            var depth = 0;
            var current = folder;

            while (current.ParentId is int parentId && byId.TryGetValue(parentId, out var parent) && depth < 100)
            {
                depth++;
                current = parent;
            }

            return depth;
        }

        foreach (var folder in folders.OrderByDescending(Depth))
        {
            _db.Folders.Remove(folder);
        }
    }

    private Task<bool> FolderExistsAsync(int userId, int folderId, CancellationToken cancellationToken)
        => _db.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == userId && !f.IsDeleted, cancellationToken);

    private async Task<bool> FolderNameExistsAsync(int userId, int? parentId, string name, int? excludeId, CancellationToken cancellationToken)
    {
        var names = await _db.Folders
            .AsNoTracking()
            .Where(f => f.OwnerId == userId && f.ParentId == parentId && !f.IsDeleted && (excludeId == null || f.Id != excludeId))
            .Select(f => f.Name)
            .ToListAsync(cancellationToken);

        return names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> FileNameExistsAsync(int userId, int? folderId, string name, int? excludeId, CancellationToken cancellationToken)
    {
        var names = await _db.Files
            .AsNoTracking()
            .Where(f => f.OwnerId == userId && f.FolderId == folderId && !f.IsDeleted && (excludeId == null || f.Id != excludeId))
            .Select(f => f.Name)
            .ToListAsync(cancellationToken);

        return names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> MakeUniqueFileNameAsync(int userId, int? folderId, string name, CancellationToken cancellationToken)
    {
        var existing = await _db.Files
            .AsNoTracking()
            .Where(f => f.OwnerId == userId && f.FolderId == folderId && !f.IsDeleted)
            .Select(f => f.Name)
            .ToListAsync(cancellationToken);

        if (!existing.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        var baseName = Path.GetFileNameWithoutExtension(name);

        for (var i = 1; i < 1000; i++)
        {
            var candidate = $"{baseName} ({i}){extension}";
            if (!existing.Any(n => string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return $"{baseName} ({Guid.NewGuid():N}){extension}";
    }

    private static string MakeSafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName).Trim();

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim(' ', '.');

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "file";
        }

        if (name.Length > 255)
        {
            var extension = Path.GetExtension(name);
            var baseName = Path.GetFileNameWithoutExtension(name);
            var maxBaseLength = Math.Max(1, 255 - extension.Length);
            name = baseName[..Math.Min(baseName.Length, maxBaseLength)] + extension;
        }

        return name;
    }

    private static string? CleanName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var name = value.Trim();

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim(' ', '.');

        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            return null;
        }

        return name.Length > 255 ? name[..255] : name;
    }

    private RedirectToActionResult RedirectToFolder(int? folderId)
        => RedirectToAction(nameof(Index), new { folderId });
}
