using SmartStudyAgent.Models;

namespace SmartStudyAgent.Services;

// VectorRagService 实现轻量本地 RAG：切分文档、计算本地向量、返回相关片段。
public sealed class VectorRagService
{
    private readonly DocumentService _documents;
    private readonly LocalEmbeddingService _embeddings;

    public VectorRagService(DocumentService documents, LocalEmbeddingService embeddings)
    {
        _documents = documents;
        _embeddings = embeddings;
    }

    public async Task<IReadOnlyList<RagChunk>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        // 查询文本先向量化，再和所有资料分块计算相似度。
        var queryVector = _embeddings.Embed(query);
        var chunks = new List<RagChunk>();
        var materials = await _documents.GetAllMaterialContentsAsync(cancellationToken);

        foreach (var material in materials)
        {
            foreach (var chunk in SplitIntoChunks(material.Content, 700, 120))
            {
                var score = _embeddings.CosineSimilarity(queryVector, _embeddings.Embed(chunk));
                if (score <= 0)
                {
                    continue;
                }

                chunks.Add(new RagChunk(material.Id, material.Title, chunk, score));
            }
        }

        return chunks
            .OrderByDescending(chunk => chunk.Score)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    private static IEnumerable<string> SplitIntoChunks(string text, int chunkSize, int overlap)
    {
        // 把长文档按固定长度切块，并保留少量重叠，减少知识点被切断的问题。
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            yield return text.Substring(start, length);

            if (start + length >= text.Length)
            {
                yield break;
            }

            start += Math.Max(1, chunkSize - overlap);
        }
    }
}
