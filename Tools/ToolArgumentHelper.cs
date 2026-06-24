namespace SmartStudyAgent.Tools;

// 负责解析多个工具都会用到的参数，避免每个工具重复写解析逻辑。
internal static class ToolArgumentHelper
{
    // materialIds 是前端选择资料后传入的逗号分隔字符串，这里转换成去重后的 ID 列表。
    public static IReadOnlyList<string> GetMaterialIds(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("materialIds", out var value) || string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
