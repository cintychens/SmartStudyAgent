using SmartStudyAgent.Models;

namespace SmartStudyAgent.Services;

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
