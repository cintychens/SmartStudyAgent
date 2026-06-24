using SmartStudyAgent.Models;
using SmartStudyAgent.Services;

namespace SmartStudyAgent.Tools;

// MaterialListTool 用于让 Agent 查看当前系统已经上传了哪些课程资料。
public sealed class MaterialListTool : IStudyTool
{
    private readonly DocumentService _documents;

    public MaterialListTool(DocumentService documents)
    {
        _documents = documents;
    }

    public string Name => "list_materials";

    public string Description => "List uploaded course materials with type, size, and upload time.";

    public async Task<ToolExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        // 从 DocumentService 获取资料摘要，并转换成适合对话展示的文本。
        var materials = await _documents.ListMaterialsAsync(cancellationToken);
        if (materials.Count == 0)
        {
            return new ToolExecutionResult(Name, "当前还没有上传课程资料。");
        }

        var lines = materials.Select((material, index) =>
            $"{index + 1}. {material.Title} [{material.FileType}] {material.CharacterCount} 字符，上传时间：{material.CreatedAt:yyyy-MM-dd HH:mm}");

        return new ToolExecutionResult(Name, string.Join(Environment.NewLine, lines));
    }
}
