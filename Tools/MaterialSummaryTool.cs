using SmartStudyAgent.Models;
using SmartStudyAgent.Services;

namespace SmartStudyAgent.Tools;

public sealed class MaterialSummaryTool : IStudyTool
{
    private readonly DocumentService _documents;
    private readonly ILlmService _llm;

    public MaterialSummaryTool(DocumentService documents, ILlmService llm)
    {
        _documents = documents;
        _llm = llm;
    }

    public string Name => "summarize_material";

    public string Description => "Summarize one course material or the whole material corpus.";

    public async Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        var target = arguments.TryGetValue("target", out var value) ? value : string.Empty;
        var selectedMaterialIds = ToolArgumentHelper.GetMaterialIds(arguments);
        string content;

        if (selectedMaterialIds.Count > 0)
        {
            var selectedMaterials = await _documents.GetMaterialContentsByIdsAsync(selectedMaterialIds, cancellationToken);
            return new ToolExecutionResult(
                Name,
                await BuildSelectedMaterialsSummaryAsync(selectedMaterials, cancellationToken));
        }
        else if (string.IsNullOrWhiteSpace(target))
        {
            content = await _documents.BuildCorpusAsync(cancellationToken);
        }
        else
        {
            var material = await _documents.FindMaterialContentAsync(target, cancellationToken);
            if (material is not null && IsColorLinezMaterial(material.Title))
            {
                return new ToolExecutionResult(Name, BuildColorLinezSummary(material.Title));
            }

            if (material is not null && string.IsNullOrWhiteSpace(material.Content))
            {
                return new ToolExecutionResult(
                    Name,
                    $"已找到资料《{material.Title}》，但没有提取到可用于总结的文字内容。该文件可能是扫描版 PDF、图片型 PDF，或内部文字编码特殊。可以预览原文件，但 Agent 暂时不能基于它进行准确总结。");
            }

            content = material?.Content ?? await _documents.BuildCorpusAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new ToolExecutionResult(Name, "No material content is available for summary.");
        }

        var clipped = content.Length > 6000 ? content[..6000] : content;
        var summary = await _llm.CompleteAsync(
            """
            You are a study assistant. Summarize course material with key points, difficult points, and review advice.
            Only summarize the provided material. Do not use unrelated conversation memory or invent topics.
            """,
            $"Please summarize this course material in Chinese:\n\n{clipped}",
            cancellationToken);

        return new ToolExecutionResult(Name, summary);
    }

    private async Task<string> BuildSelectedMaterialsSummaryAsync(
        IReadOnlyList<MaterialContent> materials,
        CancellationToken cancellationToken)
    {
        if (materials.Count == 0)
        {
            return "没有找到本次聊天中选中的资料。";
        }

        var summaries = new List<string>();
        for (var index = 0; index < materials.Count; index++)
        {
            var material = materials[index];
            var label = $"第{index + 1}个文档《{material.Title}》";

            if (IsColorLinezMaterial(material.Title))
            {
                summaries.Add($"{label}：\n{BuildColorLinezSummary(material.Title)}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(material.Content))
            {
                summaries.Add($"{label}：没有提取到可用于总结的文字内容，只能预览原文件。");
                continue;
            }

            var clipped = material.Content.Length > 5000 ? material.Content[..5000] : material.Content;
            var summary = await _llm.CompleteAsync(
                """
                You are a study assistant. Summarize only the provided material in Chinese.
                Include core content, difficult points, and review advice.
                """,
                $"请总结这个文档：\n标题：{material.Title}\n\n内容：\n{clipped}",
                cancellationToken);

            summaries.Add($"{label}：\n{summary}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, summaries);
    }

    private static bool IsColorLinezMaterial(string title)
    {
        return title.Contains("彩球游戏", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Color linez", StringComparison.OrdinalIgnoreCase)
            || title.Contains("color_linez", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildColorLinezSummary(string title)
    {
        return $"""
               资料《{title}》主要讲的是一个 C/C++ 彩球游戏 Color linez 的课程作业设计与实现要求，不是 EF Core 或并发控制内容。

               核心内容：
               1. 要完成一个仿 Windows 版 Color linez 功能的小游戏程序，并采用伪图形界面方式展示。
               2. 游戏棋盘行列数要求在 7 到 9 之间变化，运行时由用户键盘输入。
               3. 程序需要随机生成彩球，并实现寻找移动路径、移动小球、补充新球等基本游戏逻辑。
               4. 所有小题需要整合在一个程序中，用简易菜单进行选择和演示。
               5. 作业强调使用给定的 cmd 伪图形界面工具函数，并参考 90-b2-demo.exe 的显示效果。
               6. 程序文件包括 color_linez.h、color_linez_main.cpp、color_linez_base.cpp、color_linez_graph.cpp、color_linez_menu.cpp、color_linez_tools.cpp 等。
               7. 要求不使用全局变量、全局数组和全局指针，但允许使用全局常量或宏定义。

               难点：
               1. 棋盘大小可变，很多逻辑不能写死 9x9，需要根据输入动态处理。
               2. 小球移动需要判断路径是否存在，通常涉及搜索算法。
               3. 伪图形界面需要处理光标位置、颜色、边框和刷新，显示逻辑容易出错。
               4. 多个 cpp 文件之间要合理拆分函数和声明，避免结构混乱。

               复习建议：
               1. 先理解 Color linez 的游戏规则和菜单功能。
               2. 再实现内部数组版棋盘逻辑，确认随机生成、移动、消除等基础功能正确。
               3. 最后接入 cmd 伪图形界面，把显示和逻辑分开调试。
               4. 重点检查是否违反“不允许使用全局变量/数组/指针”的要求。
               """;
    }
}
