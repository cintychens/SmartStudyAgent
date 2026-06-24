using System.Text.RegularExpressions;

namespace SmartStudyAgent.Services;

// LocalEmbeddingService 是轻量本地 Embedding 实现，用哈希向量近似表示文本语义。
public sealed class LocalEmbeddingService
{
    private const int Dimensions = 128;

    public double[] Embed(string text)
    {
        // 将文本切分成中英文 token，并累加到固定维度向量中。
        var vector = new double[Dimensions];
        foreach (var token in Tokenize(text))
        {
            var index = Math.Abs(token.GetHashCode()) % Dimensions;
            vector[index] += 1.0;
        }

        var length = Math.Sqrt(vector.Sum(value => value * value));
        if (length <= 0)
        {
            return vector;
        }

        // 向量归一化后才能用余弦相似度比较文本相关性。
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= length;
        }

        return vector;
    }

    public double CosineSimilarity(double[] left, double[] right)
    {
        // 计算两个向量的余弦相似度，分数越高表示越相关。
        var sum = 0.0;
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            sum += left[index] * right[index];
        }

        return sum;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        // 中文按单字切分，英文和数字按连续词切分。
        foreach (Match match in Regex.Matches(text.ToLowerInvariant(), @"[\p{IsCJKUnifiedIdeographs}]|[a-z0-9_]+"))
        {
            yield return match.Value;
        }
    }
}
