using System.Globalization;

namespace StorageFiles.Web.Helpers;

public static class Format
{
    private static readonly string[] Units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };

    public static string Bytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 Б";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {Units[unit]}");
    }

    public static string Date(DateTime utc)
        => utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
}
