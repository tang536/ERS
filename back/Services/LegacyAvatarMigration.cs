using ERental.Data;

namespace ERental.Services;

///<summary>
/// 一次性的存量头像回填：旧版本按 `staff_{id}.jpg` / `cust_{id}.jpg` 命名，
/// 数据库里没有头像字段。升级后扫描已有文件，把路径写进 avatar_url，
/// 前端从此只读数据库 URL，不再猜文件名。
///</summary>
public static class LegacyAvatarMigration
{
    private static readonly string[] LegacyExts = [".jpg", ".jpeg", ".png", ".webp"];

    public static int Backfill(AppDbContext db, string contentRoot)
    {
        var dir = Path.Combine(contentRoot, "wwwroot", "uploads", "avatars");
        if (!Directory.Exists(dir)) return 0;

        var migrated = 0;

        foreach (var user in db.SystemUsers.Where(u => u.AvatarUrl == null).ToList())
        {
            var file = FindLegacyFile(dir, $"staff_{user.UserId}");
            if (file == null) continue;
            user.SetAvatar($"/uploads/avatars/{file}");
            migrated++;
        }

        foreach (var customer in db.Customers.Where(c => c.AvatarUrl == null).ToList())
        {
            var file = FindLegacyFile(dir, $"cust_{customer.CustomerId}");
            if (file == null) continue;
            customer.SetAvatar($"/uploads/avatars/{file}");
            migrated++;
        }

        if (migrated > 0) db.SaveChanges();
        return migrated;
    }

    private static string? FindLegacyFile(string dir, string nameWithoutExt)
    {
        foreach (var ext in LegacyExts)
        {
            var name = nameWithoutExt + ext;
            if (File.Exists(Path.Combine(dir, name))) return name;
        }
        return null;
    }
}
