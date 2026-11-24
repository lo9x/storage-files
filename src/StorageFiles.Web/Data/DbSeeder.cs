using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StorageFiles.Web.Models;
using StorageFiles.Web.Services;

namespace StorageFiles.Web.Data;

public static class DbSeeder
{
    /// <summary>
    /// Применяет миграции и создаёт администратора, если его ещё нет.
    /// Данные берутся из секции Seed в appsettings.
    /// </summary>
    public static async Task InitializeAsync(
        AppDbContext db,
        IConfiguration configuration,
        IPasswordHasher<User> passwordHasher,
        StorageOptions storageOptions)
    {
        await db.Database.MigrateAsync();

        var email = (configuration["Seed:AdminEmail"] ?? "admin@storage.local").Trim().ToLowerInvariant();
        var password = configuration["Seed:AdminPassword"] ?? "Admin123!";

        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return;
        }

        var admin = new User
        {
            Email = email,
            DisplayName = "Администратор",
            Role = RoleNames.Admin,
            StorageLimitBytes = storageOptions.DefaultQuotaBytes,
            CreatedAt = DateTime.UtcNow
        };
        admin.PasswordHash = passwordHasher.HashPassword(admin, password);

        db.Users.Add(admin);
        await db.SaveChangesAsync();
    }
}
