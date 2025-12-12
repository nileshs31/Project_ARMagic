// DtwKnnRecognizer.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[Serializable]
public class DtwTemplate
{
    public string name;
    public float[] flattenedPts; // x0,y0, x1,y1, ... length == 2 * N
    public int N;
}

public class DtwKnnRecognizer
{
    // configuration
    public int resampleLength = 64;         // N: number of timesteps to resample to
    public float minSampleDistance = 0.004f; // sampling down from raw
    public float sakoeRatio = 0.2f;         // fraction of N used as warping band
    public int k = 3;                       // k for k-NN
    public float acceptThreshold = 2.5f;    // max average DTW score to accept; tweak per project

    List<DtwTemplate> templates = new List<DtwTemplate>();

    // --- Public API ---

    // Add a template from a world-space stroke (List<Vector3>) with a given name
    public void AddTemplate(string name, List<Vector3> strokeWorld)
    {
        var pts2 = ProjectToPlane(strokeWorld);
        var sampled = ResampleByDistance2D(pts2, minSampleDistance, 1024);
        var resampled = ResampleToFixed(sampled, resampleLength);
        var normalized = NormalizeSequence(resampled);

        var flattened = new float[2 * resampleLength];
        for (int i = 0; i < resampleLength; ++i)
        {
            flattened[2 * i] = normalized[i].x;
            flattened[2 * i + 1] = normalized[i].y;
        }

        templates.Add(new DtwTemplate { name = name, flattenedPts = flattened, N = resampleLength });
    }

    // Recognize a stroke; returns label (or "Unknown") and a confidence-ish score (lower is better)
    public (string label, float score, float margin) Recognize(List<Vector3> strokeWorld)
    {
        if (templates == null || templates.Count == 0) return ("NoTemplates", float.MaxValue, 0f);
        var pts2 = ProjectToPlane(strokeWorld);
        var sampled = ResampleByDistance2D(pts2, minSampleDistance, 1024);
        var resampled = ResampleToFixed(sampled, resampleLength);
        var normalized = NormalizeSequence(resampled);

        float[] seq = new float[2 * resampleLength];
        for (int i = 0; i < resampleLength; ++i)
        {
            seq[2 * i] = normalized[i].x;
            seq[2 * i + 1] = normalized[i].y;
        }

        // compute DTW distances to all templates
        int window = Mathf.Max(1, Mathf.FloorToInt(resampleLength * sakoeRatio));
        var scored = new List<(string name, float dist)>();
        foreach (var t in templates)
        {
            float d = DTWDistance(seq, t.flattenedPts, resampleLength, window);
            scored.Add((t.name, d));
        }

        // k-NN majority vote by smallest distances
        var ordered = scored.OrderBy(x => x.dist).ToList();
        var topK = ordered.Take(k).ToList();
        // majority vote
        var groups = topK.GroupBy(x => x.name).OrderByDescending(g => g.Count()).ToList();
        string chosen = groups[0].Key;
        // score = average distance among top matches of chosen class
        var chosenDists = topK.Where(x => x.name == chosen).Select(x => x.dist).ToList();
        float avgScore = chosenDists.Average();
        // margin: difference between best class avg dist and second best class avg dist (positive = confident)
        float secondBest = float.MaxValue;
        if (groups.Count > 1)
        {
            var g2 = groups[1];
            secondBest = topK.Where(x => x.name == g2.Key).Select(x => x.dist).DefaultIfEmpty(float.MaxValue).Average();
        }
        float margin = secondBest - avgScore;

        // acceptance threshold (lower is better): if avgScore too large -> Unknown
        if (avgScore > acceptThreshold) return ("Unknown", avgScore, margin);

        return (chosen, avgScore, margin);
    }

    // Save templates as JSON (useful to persist)
    public void SaveTemplates(string path)
    {
        var json = JsonUtility.ToJson(new Wrapper { templates = templates.ToArray() }, prettyPrint: true);
        File.WriteAllText(path, json);
        Debug.Log($"DtwKnnRecognizer: Saved {templates.Count} templates to {path}");
    }

    // Load templates from JSON (overwrites current templates)
    public void LoadTemplates(string path)
    {
        if (!File.Exists(path)) { Debug.LogWarning("Template file missing: " + path); return; }
        var text = File.ReadAllText(path);
        var w = JsonUtility.FromJson<Wrapper>(text);
        templates = w.templates != null ? new List<DtwTemplate>(w.templates) : new List<DtwTemplate>();
        Debug.Log($"DtwKnnRecognizer: Loaded {templates.Count} templates from {path}");
    }

    [Serializable]
    class Wrapper { public DtwTemplate[] templates; }

    // Remove all templates
    public void ClearTemplates() => templates.Clear();

    // Get template names (for UI)
    public List<string> GetTemplateNames() => templates.Select(t => t.name).Distinct().ToList();

    // --- Implementation details below ---

    // Projects the world-space stroke to a 2D plane using cross-sum normal heuristic and returns list of Vector2
    List<Vector2> ProjectToPlane(List<Vector3> world)
    {
        var outPts = new List<Vector2>();
        if (world == null || world.Count == 0) return outPts;

        // estimate normal using cross-sum
        Vector3 normal = Vector3.zero;
        for (int i = 2; i < world.Count; i++)
        {
            Vector3 a = world[i - 2], b = world[i - 1], c = world[i];
            normal += Vector3.Cross(b - a, c - b);
        }
        if (normal.sqrMagnitude < 1e-8f) normal = Camera.main ? Camera.main.transform.forward : Vector3.up;
        normal.Normalize();

        Vector3 u = (world[world.Count - 1] - world[0]);
        if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(normal, Vector3.up);
        u = Vector3.ProjectOnPlane(u, normal).normalized;
        Vector3 v = Vector3.Cross(normal, u).normalized;
        Vector3 origin = world[0];

        foreach (var p in world)
        {
            Vector3 d = p - origin;
            outPts.Add(new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v)));
        }
        return outPts;
    }

    // Resample points by distance (2D), useful to drop jitter and reduce samples
    List<Vector2> ResampleByDistance2D(List<Vector2> pts, float minDist, int maxPoints)
    {
        var outPts = new List<Vector2>();
        if (pts == null || pts.Count == 0) return outPts;
        outPts.Add(pts[0]);
        Vector2 last = pts[0];
        for (int i = 1; i < pts.Count && outPts.Count < maxPoints; i++)
        {
            if ((pts[i] - last).sqrMagnitude >= minDist * minDist)
            {
                outPts.Add(pts[i]);
                last = pts[i];
            }
        }
        if ((outPts.Count == 0) || (outPts[outPts.Count - 1] != pts[pts.Count - 1]))
            outPts.Add(pts[pts.Count - 1]);
        return outPts;
    }

    // Resample sequence to exactly N points by linear interpolation along arc-length
    List<Vector2> ResampleToFixed(List<Vector2> pts, int N)
    {
        var outPts = new List<Vector2>();
        if (pts == null || pts.Count == 0)
        {
            for (int i = 0; i < N; i++) outPts.Add(Vector2.zero);
            return outPts;
        }
        float pathLen = 0f;
        for (int i = 1; i < pts.Count; i++) pathLen += Vector2.Distance(pts[i - 1], pts[i]);
        if (pathLen <= 0f)
        {
            for (int i = 0; i < N; i++) outPts.Add(pts[0]);
            return outPts;
        }
        float I = pathLen / (N - 1);
        float D = 0f;
        outPts.Add(pts[0]);
        int idx = 1;
        while (outPts.Count < N && idx < pts.Count)
        {
            float d = Vector2.Distance(pts[idx - 1], pts[idx]);
            if (D + d >= I - 1e-6f)
            {
                float t = (I - D) / d;
                Vector2 q = Vector2.Lerp(pts[idx - 1], pts[idx], t);
                outPts.Add(q);
                // insert q as new previous point by updating pts[idx - 1]
                pts[idx - 1] = q;
                D = 0f;
            }
            else
            {
                D += d;
                idx++;
                if (idx == pts.Count && outPts.Count < N)
                {
                    // pad remaining with last point
                    while (outPts.Count < N) outPts.Add(pts[pts.Count - 1]);
                }
            }
        }
        // safety
        while (outPts.Count < N) outPts.Add(pts[pts.Count - 1]);
        if (outPts.Count > N) outPts.RemoveRange(N, outPts.Count - N);
        return outPts;
    }

    // Normalize: translate centroid to origin, scale bounding box to unit size (keeps aspect ratio)
    List<Vector2> NormalizeSequence(List<Vector2> seq)
    {
        var outSeq = new List<Vector2>(seq.Count);
        Vector2 centroid = Vector2.zero;
        foreach (var p in seq) centroid += p;
        centroid /= seq.Count;

        float minx = float.MaxValue, maxx = float.MinValue, miny = float.MaxValue, maxy = float.MinValue;
        foreach (var p in seq)
        {
            if (p.x < minx) minx = p.x;
            if (p.x > maxx) maxx = p.x;
            if (p.y < miny) miny = p.y;
            if (p.y > maxy) maxy = p.y;
        }
        float scale = Mathf.Max(maxx - minx, maxy - miny);
        if (scale < 1e-6f) scale = 1f;

        foreach (var p in seq)
            outSeq.Add((p - centroid) / scale);

        return outSeq;
    }

    // DTW distance for two flattened sequences [x0,y0, x1,y1, ...], length = 2*N
    float DTWDistance(float[] s1, float[] s2, int N, int window)
    {
        // cost matrix (2D) can be optimized to 1D but N small so simple implementation is fine
        int W = Math.Max(window, Math.Abs(N - N)); // symmetric in our case
        float INF = 1e9f;
        var dtw = new float[N + 1, N + 1];
        for (int i = 0; i <= N; i++)
            for (int j = 0; j <= N; j++)
                dtw[i, j] = INF;
        dtw[0, 0] = 0f;

        for (int i = 1; i <= N; ++i)
        {
            int start = Math.Max(1, i - W);
            int end = Math.Min(N, i + W);
            for (int j = start; j <= end; ++j)
            {
                float cost = Euclidean2D(s1, s2, i - 1, j - 1);
                float minPrev = Mathf.Min(dtw[i - 1, j], Mathf.Min(dtw[i, j - 1], dtw[i - 1, j - 1]));
                dtw[i, j] = cost + minPrev;
            }
        }
        return dtw[N, N] / N; // normalized by length
    }

    // Euclidean distance between timestep i in s1 and j in s2 (both flattened 2D)
    static float Euclidean2D(float[] s1, float[] s2, int i, int j)
    {
        float dx = s1[2 * i] - s2[2 * j];
        float dy = s1[2 * i + 1] - s2[2 * j + 1];
        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}
