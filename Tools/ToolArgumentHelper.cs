namespace SmartStudyAgent.Tools;

internal static class ToolArgumentHelper
{
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
