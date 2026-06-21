using System.Text.RegularExpressions;

namespace SmartStudyAgent.Services;

public sealed class LocalEmbeddingService
{
    private const int Dimensions = 128;

    public double[] Embed(string text)
    {
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

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= length;
        }

        return vector;
    }

    public double CosineSimilarity(double[] left, double[] right)
    {
        var sum = 0.0;
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            sum += left[index] * right[index];
        }

        return sum;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (Match match in Regex.Matches(text.ToLowerInvariant(), @"[\p{IsCJKUnifiedIdeographs}]|[a-z0-9_]+"))
        {
            yield return match.Value;
        }
    }
}
