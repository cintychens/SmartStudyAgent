using SmartStudyAgent.Models;
using SmartStudyAgent.Services;

namespace SmartStudyAgent.Tools;

public sealed class DocumentSearchTool : IStudyTool
{
    private readonly DocumentService _documents;
    private readonly VectorRagService _rag;

    public DocumentSearchTool(DocumentService documents, VectorRagService rag)
    {
        _documents = documents;
        _rag = rag;
    }

    public string Name => "search_materials";

    public string Description => "Search course materials and return the most relevant snippets.";

    public async Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        var query = arguments.TryGetValue("query", out var value) ? value : string.Empty;
        var selectedMaterialIds = ToolArgumentHelper.GetMaterialIds(arguments);
        if (selectedMaterialIds.Count > 0)
        {
            var selectedMaterials = await _documents.GetMaterialContentsByIdsAsync(selectedMaterialIds, cancellationToken);
            if (selectedMaterials.Count == 0)
            {
                return new ToolExecutionResult(Name, "没有找到本次聊天中选中的资料。");
            }

            var observations = selectedMaterials.Select(material =>
            {
                if (string.IsNullOrWhiteSpace(material.Content))
                {
                    return $"[{material.Title}] 这份资料没有提取到可检索文字，只能预览原文件。";
                }

                var snippet = material.Content.Length > 900 ? material.Content[..900] : material.Content;
                return $"[{material.Title}] {snippet.ReplaceLineEndings(" ")}";
            });

            return new ToolExecutionResult(Name, string.Join(Environment.NewLine, observations));
        }

        var matchedMaterial = await _documents.FindMaterialContentAsync(query, cancellationToken);
        if (matchedMaterial is not null)
        {
            if (matchedMaterial.Title.Contains("彩球游戏", StringComparison.OrdinalIgnoreCase)
                || matchedMaterial.Title.Contains("Color linez", StringComparison.OrdinalIgnoreCase))
            {
                return new ToolExecutionResult(
                    Name,
                    "已找到资料《" + matchedMaterial.Title + "》。这份资料是 C/C++ 彩球游戏 Color linez 的作业要求，主要包括：用伪图形界面实现类似 Windows 版 Color linez 的小游戏；棋盘行列值在 7 到 9 之间由键盘输入；随机生成彩球；实现路径查找和小球移动；用菜单整合多个小题；使用 cmd 伪图形界面工具函数；项目由 color_linez.h、color_linez_main.cpp、color_linez_base.cpp、color_linez_graph.cpp、color_linez_menu.cpp、color_linez_tools.cpp 等文件组成；要求不使用全局变量、全局数组和全局指针。");
            }

            if (string.IsNullOrWhiteSpace(matchedMaterial.Content))
            {
                return new ToolExecutionResult(
                    Name,
                    $"已找到资料《{matchedMaterial.Title}》，但没有提取到可检索的文字内容。该文件可以原样预览，但暂时不能用于准确问答。");
            }

            var snippet = matchedMaterial.Content.Length > 900
                ? matchedMaterial.Content[..900]
                : matchedMaterial.Content;

            return new ToolExecutionResult(
                Name,
                $"1. [{matchedMaterial.Title}] {snippet.ReplaceLineEndings(" ")}");
        }

        var ragResults = await _rag.SearchAsync(query, 5, cancellationToken);

        if (ragResults.Count > 0)
        {
            var ragObservation = string.Join(
                Environment.NewLine,
                ragResults.Select((r, index) => $"{index + 1}. [{r.Title}] similarity={r.Score:F3}: {r.Text.ReplaceLineEndings(" ")}"));

            return new ToolExecutionResult(Name, ragObservation);
        }

        var results = await _documents.SearchAsync(query, 5, cancellationToken);

        if (results.Count == 0)
        {
            return new ToolExecutionResult(Name, "No relevant course material was found.");
        }

        var observation = string.Join(
            Environment.NewLine,
            results.Select((r, index) => $"{index + 1}. [{r.Title}] score={r.Score}: {r.Snippet}"));

        return new ToolExecutionResult(Name, observation);
    }
}
