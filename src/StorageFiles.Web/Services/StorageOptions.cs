namespace StorageFiles.Web.Services;

public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Каталог для файлов пользователей. Относительный путь считается от ContentRoot.</summary>
    public string RootPath { get; set; } = "App_Data/storage";

    /// <summary>Лимит хранения нового пользователя (2 ГБ по умолчанию).</summary>
    public long DefaultQuotaBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    public int MaxFileSizeMb { get; set; } = 100;

    public int MaxFilesPerUpload { get; set; } = 20;
}
