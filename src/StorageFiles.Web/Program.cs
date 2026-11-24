using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StorageFiles.Web.Data;
using StorageFiles.Web.Models;
using StorageFiles.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// --- База данных (SQLite) ---
var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? "Data Source=App_Data/storagefiles.db";
var sqlite = new SqliteConnectionStringBuilder(connectionString);
if (!Path.IsPathRooted(sqlite.DataSource))
{
    sqlite.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource);
}
var dbDirectory = Path.GetDirectoryName(sqlite.DataSource);
if (!string.IsNullOrEmpty(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(sqlite.ConnectionString));

// --- Файловое хранилище ---
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddSingleton<StorageService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// --- Аутентификация через cookie ---
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "storagefiles.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();

// Ограничения на размер загружаемых файлов (100 МБ на файл, 20 файлов за раз).
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 210L * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 210L * 1024 * 1024;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Миграции БД и создание администратора при первом запуске.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();
    var hasher = services.GetRequiredService<IPasswordHasher<User>>();
    var storage = services.GetRequiredService<StorageService>();
    await DbSeeder.InitializeAsync(db, app.Configuration, hasher, storage.Options);
}

app.Run();
