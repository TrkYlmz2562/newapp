namespace FocusAI.Domain.Text;

/// <summary>Small vector helpers used by clustering, related-story lookup and RAG.</summary>
public static class VectorMath
{
    /// <summary>
    /// Cosine similarity in [-1,1]. Returns 0 for empty, mismatched or
    /// zero-magnitude inputs so callers never have to null-check.
    /// </summary>
    public static double CosineSimilarity(float[]? left, float[]? right)
    {
        if (left is null || right is null || left.Length == 0 || left.Length != right.Length)
        {
            return 0d;
        }

        double dot = 0d, leftMagnitude = 0d, rightMagnitude = 0d;

        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * (double)right[i];
            leftMagnitude += left[i] * (double)left[i];
            rightMagnitude += right[i] * (double)right[i];
        }

        if (leftMagnitude <= double.Epsilon || rightMagnitude <= double.Epsilon)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    /// <summary>Element-wise mean. Used for story centroids and user taste vectors.</summary>
    public static float[]? Centroid(IReadOnlyCollection<float[]> vectors)
    {
        var usable = vectors.Where(v => v.Length > 0).ToArray();
        if (usable.Length == 0)
        {
            return null;
        }

        var dimensions = usable[0].Length;
        if (usable.Any(v => v.Length != dimensions))
        {
            // Mixed embedding models in one cluster — refuse rather than emit garbage.
            return null;
        }

        var sum = new double[dimensions];
        foreach (var vector in usable)
        {
            for (var i = 0; i < dimensions; i++)
            {
                sum[i] += vector[i];
            }
        }

        var result = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
        {
            result[i] = (float)(sum[i] / usable.Length);
        }

        return result;
    }

    /// <summary>L2-normalises in place-safe fashion, returning a new array.</summary>
    public static float[] Normalize(float[] vector)
    {
        double magnitude = 0d;
        foreach (var value in vector)
        {
            magnitude += value * (double)value;
        }

        magnitude = Math.Sqrt(magnitude);
        if (magnitude <= double.Epsilon)
        {
            return vector;
        }

        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            result[i] = (float)(vector[i] / magnitude);
        }

        return result;
    }
}
