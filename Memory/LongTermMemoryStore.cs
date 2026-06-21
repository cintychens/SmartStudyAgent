using System.Text.Json;
using SmartStudyAgent.Models;

namespace SmartStudyAgent.Memory;

public sealed class LongTermMemoryStore
{
    private readonly string _filePath;
    private readonly Dictionary<string, LongTermMemoryRecord> _records = new();
    private readonly object _lock = new();

    public LongTermMemoryStore(IWebHostEnvironment environment)
    {
        var directory = Path.Combine(environment.ContentRootPath, "Data", "Memory");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "long-term-memory.json");
        Load();
    }

    public LongTermMemoryRecord Get(string sessionId)
    {
        lock (_lock)
        {
            return _records.TryGetValue(sessionId, out var record)
                ? record
                : new LongTermMemoryRecord(sessionId, null, null, DateTimeOffset.UtcNow);
        }
    }

    public LongTermMemoryRecord Update(string sessionId, string? learningGoal, string? preference)
    {
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
            // Keep app usable when memory file is unavailable.
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_records.Values, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
