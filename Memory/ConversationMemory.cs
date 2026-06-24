using SmartStudyAgent.Models;

namespace SmartStudyAgent.Memory;

// ConversationMemory 保存短期会话记忆，用于多轮对话和会话切换。
public sealed class ConversationMemory
{
    private readonly string _memoryFilePath;
    private readonly Dictionary<string, List<ChatMessageRecord>> _sessions = new();
    private readonly object _lock = new();

    public ConversationMemory(IWebHostEnvironment environment)
    {
        // Memory 数据保存在 Data/Memory 下，程序重启后仍能恢复历史会话。
        var directory = Path.Combine(environment.ContentRootPath, "Data", "Memory");
        Directory.CreateDirectory(directory);
        _memoryFilePath = Path.Combine(directory, "conversation-memory.json");
        Load();
    }

    public void Add(string sessionId, string role, string content)
    {
        // 线程锁保证多个请求同时写入时不会破坏内存字典和本地文件。
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var messages))
            {
                messages = new List<ChatMessageRecord>();
                _sessions[sessionId] = messages;
            }

            messages.Add(new ChatMessageRecord(role, content, DateTimeOffset.UtcNow));
            Save();
        }
    }

    public IReadOnlyList<ChatMessageRecord> GetMessages(string sessionId)
    {
        // 返回副本，避免外部代码直接修改内部集合。
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId, out var messages)
                ? messages.ToList()
                : Array.Empty<ChatMessageRecord>();
        }
    }

    public string BuildContext(string sessionId, int takeLast = 8)
    {
        // 只取最近几条消息拼接给 LLM，既保留上下文又避免提示词过长。
        var messages = GetMessages(sessionId).TakeLast(takeLast);
        return string.Join(Environment.NewLine, messages.Select(m => $"{m.Role}: {m.Content}"));
    }

    public IReadOnlyList<SessionInfo> ListSessions()
    {
        // 生成会话列表摘要，前端下拉框用它展示多会话。
        lock (_lock)
        {
            return _sessions
                .Select(pair =>
                {
                    var last = pair.Value.LastOrDefault();
                    return new SessionInfo(
                        pair.Key,
                        pair.Value.Count,
                        last?.CreatedAt ?? DateTimeOffset.MinValue,
                        last?.Content.Length > 60 ? last.Content[..60] + "..." : last?.Content ?? string.Empty);
                })
                .OrderByDescending(session => session.UpdatedAt)
                .ToList();
        }
    }

    public void Clear(string sessionId)
    {
        // 清空指定会话的短期记忆，不影响其他会话和长期记忆。
        lock (_lock)
        {
            _sessions.Remove(sessionId);
            Save();
        }
    }

    private void Load()
    {
        // 启动时从本地 JSON 文件恢复会话记忆。
        if (!File.Exists(_memoryFilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_memoryFilePath);
            var sessions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<ChatMessageRecord>>>(json);
            if (sessions is null)
            {
                return;
            }

            foreach (var session in sessions)
            {
                _sessions[session.Key] = session.Value;
            }
        }
        catch
        {
            // 如果记忆文件损坏，就用空记忆继续运行，避免整个系统启动失败。
        }
    }

    private void Save()
    {
        // 每次变更后立即落盘，保证刷新页面或重启后仍能读取历史。
        var json = System.Text.Json.JsonSerializer.Serialize(_sessions, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_memoryFilePath, json);
    }
}
