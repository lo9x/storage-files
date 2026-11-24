using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StorageFiles.Web.Data;
using StorageFiles.Web.Helpers;
using StorageFiles.Web.ViewModels;

namespace StorageFiles.Web.ViewComponents;

/// <summary>Карточка занятого места в боковой панели.</summary>
public class StorageQuotaViewComponent : ViewComponent
{
    private readonly AppDbContext _db;

    public StorageQuotaViewComponent(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = UserClaimsPrincipal.GetUserId();

        var used = await _db.Files
            .Where(f => f.OwnerId == userId && !f.IsDeleted)
            .SumAsync(f => (long?)f.SizeBytes) ?? 0;

        var limit = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.StorageLimitBytes)
            .FirstOrDefaultAsync();

        return View(new QuotaViewModel { UsedBytes = used, LimitBytes = limit });
    }
}
