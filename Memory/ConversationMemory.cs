using SmartStudyAgent.Models;

namespace SmartStudyAgent.Memory;

public sealed class ConversationMemory
{
    private readonly string _memoryFilePath;
    private readonly Dictionary<string, List<ChatMessageRecord>> _sessions = new();
    private readonly object _lock = new();

    public ConversationMemory(IWebHostEnvironment environment)
    {
        var directory = Path.Combine(environment.ContentRootPath, "Data", "Memory");
        Directory.CreateDirectory(directory);
        _memoryFilePath = Path.Combine(directory, "conversation-memory.json");
        Load();
    }

    public void Add(string sessionId, string role, string content)
    {
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
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId, out var messages)
                ? messages.ToList()
                : Array.Empty<ChatMessageRecord>();
        }
    }

    public string BuildContext(string sessionId, int takeLast = 8)
    {
        var messages = GetMessages(sessionId).TakeLast(takeLast);
        return string.Join(Environment.NewLine, messages.Select(m => $"{m.Role}: {m.Content}"));
    }

    public IReadOnlyList<SessionInfo> ListSessions()
    {
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
        lock (_lock)
        {
            _sessions.Remove(sessionId);
            Save();
        }
    }

    private void Load()
    {
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
            // If persisted memory is damaged, keep the app usable with an empty memory.
        }
    }

    private void Save()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(_sessions, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_memoryFilePath, json);
    }
}
