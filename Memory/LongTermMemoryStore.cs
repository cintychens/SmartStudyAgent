using System.Text.Json;
using SmartStudyAgent.Models;

namespace SmartStudyAgent.Memory;

// LongTermMemoryStore 保存长期学习信息，例如学习目标和偏好，独立于聊天记录。
public sealed class LongTermMemoryStore
{
    private readonly string _filePath;
    private readonly Dictionary<string, LongTermMemoryRecord> _records = new();
    private readonly object _lock = new();

    public LongTermMemoryStore(IWebHostEnvironment environment)
    {
        // 长期记忆也保存到 Data/Memory，和短期会话记忆分开存储。
        var directory = Path.Combine(environment.ContentRootPath, "Data", "Memory");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "long-term-memory.json");
        Load();
    }

    public LongTermMemoryRecord Get(string sessionId)
    {
        // 没有长期记忆时返回一个空记录，避免调用方判断 null。
        lock (_lock)
        {
            return _records.TryGetValue(sessionId, out var record)
                ? record
                : new LongTermMemoryRecord(sessionId, null, null, DateTimeOffset.UtcNow);
        }
    }

    public LongTermMemoryRecord Update(string sessionId, string? learningGoal, string? preference)
    {
        // 更新学习目标和偏好，并立刻写入本地 JSON 文件。
        lock (_lock)
        {
            var record = new LongTermMemoryRecord(sessionId, learningGoal, preference, DateTimeOffset.UtcNow);
            _records[sessionId] = record;
            Save();
            return record;
        }
    }

    private void Load()
    {
        // 程序启动时加载已有长期记忆。
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var records = JsonSerializer.Deserialize<List<LongTermMemoryRecord>>(json) ?? new List<LongTermMemoryRecord>();
            foreach (var record in records)
            {
                _records[record.SessionId] = record;
            }
        }
        catch
        {
            // 如果长期记忆文件不可用，系统仍然可以继续运行。
        }
    }

    private void Save()
    {
        // 将当前长期记忆集合保存到磁盘。
        var json = JsonSerializer.Serialize(_records.Values, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
