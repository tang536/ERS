using System.Globalization;
using ERental.Models;

namespace ERental.Services;

///<summary>数值型系统配置的安全读取</summary>
internal static class ConfigValue
{
    ///<summary>读取小数型配置</summary>
    public static decimal Decimal(IEnumerable<SystemConfig> configs, string key, decimal fallback)
    {
        var raw = configs.FirstOrDefault(c => c.ConfigKey == key)?.ConfigValue;
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        //配置值由本系统按不变文化写入，读取时也必须用不变文化：
        //服务器区域设置若把「,」当小数点，「12.50」会被解析成 1250，金额直接错位。
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : throw new BusinessException($"系统配置「{key}」当前值为“{raw}”，不是有效数字，请先在「系统配置」页面修正后再操作");
    }

    ///<summary>读取整数型配置</summary>
    public static int Int(IEnumerable<SystemConfig> configs, string key, int fallback)
    {
        var raw = configs.FirstOrDefault(c => c.ConfigKey == key)?.ConfigValue;
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw new BusinessException($"系统配置「{key}」当前值为“{raw}”，不是有效整数，请先在「系统配置」页面修正后再操作");
    }
}
