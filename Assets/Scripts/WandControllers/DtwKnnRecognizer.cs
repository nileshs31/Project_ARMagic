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
    // ── configuration ────────────────────────────────────────────────────────
    public int   resampleLength  = 64;    // N: points to resample every stroke to
    public float minSampleDistance = 0.004f; // pre-filter raw samples
    public float sakoeRatio      = 0.2f;  // Sakoe-Chiba warping band (fraction of N)
    public int   k               = 5;     // k-NN neighbours  (was 3 – more robust vote)
    public float acceptThreshold = 1.5f;  // max DTW/N to accept  (was 5.5 – 5.5 accepts everything;
                                          // normalised scores max at ~1.41 so 1.5 is already generous)
    public float minMargin       = 0.2f;  // min gap between best/2nd-best class (was 0.3)

    // ── PROJECTION NOTE ──────────────────────────────────────────────────────
    // ProjectToPlane now uses the camera's view plane (camera.right / camera.up).
    // This fixes two bugs in the old cross-sum approach:
    //   1. swipe_up and swipe_down projected to the SAME 2D shape (both became a
    //      horizontal line) because the u-axis was derived from the stroke direction,
    //      cancelling out the very information needed to tell them apart.
    //   2. Circles used the tiny start→end vector as their u-axis (~2-8 mm, random
    //      direction each draw) making the coordinate frame inconsistent.
    //
    // ⚠ RETRAIN REQUIRED: gesture_templates.json was recorded with the old
    //   projection.  Delete it (or clear via editor) and re-record all gestures.
    //   Use GestureQuickRecorderEditor buttons – takes ~15-20 min.
    // ─────────────────────────────────────────────────────────────────────────

    List<DtwTemplate> templates = new List<DtwTemplate>();

    // ── Public API ────────────────────────────────────────────────────────────

    public void AddTemplate(string name, List<Vector3> strokeWorld)
    {
        var pts2       = ProjectToPlane(strokeWorld);
        var sampled    = ResampleByDistance2D(pts2, minSampleDistance, 1024);
        var resampled  = ResampleToFixed(sampled, resampleLength);
        var normalized = NormalizeSequence(resampled);

        var flattened = new float[2 * resampleLength];
        for (int i = 0; i < resampleLength; ++i)
        {
            flattened[2 * i]     = normalized[i].x;
            flattened[2 * i + 1] = normalized[i].y;
        }

        templates.Add(new DtwTemplate { name = name, flattenedPts = flattened, N = resampleLength });
    }

    // Returns (label, score, margin).  label == "Unknown" means rejected.
    public (string label, float score, float margin) Recognize(List<Vector3> strokeWorld)
    {
        if (templates == null || templates.Count == 0)
            return ("NoTemplates", float.MaxValue, 0f);

        // ── 1. Directional features from raw 3D points ───────────────────────
        // Both computed before any 2D projection so they are independent of the
        // projection method and are used to override DTW for directional gestures.

        // Winding sign: > 0 = CCW from viewer, < 0 = CW  (used for circles)
        float windingSign = ComputeWindingSign3D(strokeWorld);

        // Stroke net direction in camera space: x = horizontal, y = vertical
        // (used for swipe_up / swipe_down)
        Vector2 strokeDir = ComputeStrokeDirection(strokeWorld);

        // ── 2. Project → resample → normalise ────────────────────────────────
        var pts2       = ProjectToPlane(strokeWorld);
        var sampled    = ResampleByDistance2D(pts2, minSampleDistance, 1024);
        var resampled  = ResampleToFixed(sampled, resampleLength);
        var normalized = NormalizeSequence(resampled);

        float[] seq = new float[2 * resampleLength];
        for (int i = 0; i < resampleLength; ++i)
        {
            seq[2 * i]     = normalized[i].x;
            seq[2 * i + 1] = normalized[i].y;
        }

        // ── 3. DTW distance to every template ────────────────────────────────
        int window = Mathf.Max(1, Mathf.FloorToInt(resampleLength * sakoeRatio));
        var scored = new List<(string name, float dist)>(templates.Count);
        foreach (var t in templates)
        {
            float d = DTWDistance(seq, t.flattenedPts, resampleLength, window);
            scored.Add((t.name, d));
        }

        // ── 4. k-NN majority vote ─────────────────────────────────────────────
        var ordered = scored.OrderBy(x => x.dist).ToList();
        var topK    = ordered.Take(k).ToList();

        var groups  = topK.GroupBy(x => x.name)
                          .OrderByDescending(g => g.Count())
                          .ThenBy(g => topK.Where(x => x.name == g.Key).Average(x => x.dist))
                          .ToList();

        string chosen   = groups[0].Key;
        float  avgScore = topK.Where(x => x.name == chosen).Average(x => x.dist);

        float secondBest = float.MaxValue;
        if (groups.Count > 1)
        {
            string g2Name   = groups[1].Key;
            var    g2Dists  = topK.Where(x => x.name == g2Name).ToList();
            secondBest      = g2Dists.Count > 0 ? g2Dists.Average(x => x.dist) : float.MaxValue;
        }
        float margin = secondBest - avgScore;

        // ── 5. Accept / reject ────────────────────────────────────────────────

        // Hard score reject – the stroke doesn't look like anything trained
        if (avgScore > acceptThreshold)
            return ("Unknown", avgScore, margin);

        // ── Circle direction override ─────────────────────────────────────────
        // DTW tells us "it looks like a circle"; winding sign tells us which way.
        // Margin check is skipped: CW/CCW are the same shape in opposite directions
        // so a small margin between them is expected and correct — winding resolves it.
        if (chosen == "circle_cw" || chosen == "circle_ccw")
        {
            if (windingSign != 0f)
                chosen = (windingSign > 0f) ? "circle_ccw" : "circle_cw";
            return (chosen, avgScore, margin);
        }

        // ── Swipe direction override ──────────────────────────────────────────
        // DTW identifies "it looks like a swipe"; the net vertical component of the
        // stroke in camera space tells us which way.  Same reasoning as circles:
        // swipe_up and swipe_down are mirror images so margin between them can be
        // small; the geometric direction is definitive.
        if (chosen == "swipe_up" || chosen == "swipe_down")
        {
            chosen = (strokeDir.y >= 0f) ? "swipe_up" : "swipe_down";
            return (chosen, avgScore, margin);
        }

        // ── All other gestures: require a clear margin ────────────────────────
        if (margin < minMargin)
            return ("Unknown", avgScore, margin);

        return (chosen, avgScore, margin);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    public void SaveTemplates(string path)
    {
        var json = JsonUtility.ToJson(new Wrapper { templates = templates.ToArray() }, prettyPrint: true);
        File.WriteAllText(path, json);
        Debug.Log($"DtwKnnRecognizer: Saved {templates.Count} templates to {path}");
    }

    public void LoadTemplates(string path)
    {
        if (!File.Exists(path)) { Debug.LogWarning("Template file missing: " + path); return; }
        var text = File.ReadAllText(path);
        var w    = JsonUtility.FromJson<Wrapper>(text);
        templates = w.templates != null ? new List<DtwTemplate>(w.templates) : new List<DtwTemplate>();
        Debug.Log($"DtwKnnRecognizer: Loaded {templates.Count} templates from {path}");
    }

    public void LoadTemplatesFromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("LoadTemplatesFromJson: JSON is empty.");
            templates = new List<DtwTemplate>();
            return;
        }
        var w = JsonUtility.FromJson<Wrapper>(json);
        templates = w.templates != null
            ? new List<DtwTemplate>(w.templates)
            : new List<DtwTemplate>();
        Debug.Log($"DtwKnnRecognizer: Loaded {templates.Count} templates from JSON");
    }

    [Serializable] class Wrapper { public DtwTemplate[] templates; }

    public void ClearTemplates() => templates.Clear();
    public List<string> GetTemplateNames() => templates.Select(t => t.name).Distinct().ToList();

    // ── Projection ────────────────────────────────────────────────────────────

    // Projects world-space stroke onto the camera's view plane using camera.right (u)
    // and camera.up (v) as the 2D axes.
    //
    // Why camera-aligned instead of stroke-derived?
    //   • Swipes: the old method set u = stroke direction, which CANCELLED the very
    //     info needed to tell up from down – both projected to the same rightward line.
    //   • Circles: the old method used start→end (~2-8 mm random gap) as u, giving a
    //     different coordinate frame every draw.
    //   Camera axes are the same for every gesture drawn from a natural wrist position
    //   in front of the headset.
    List<Vector2> ProjectToPlane(List<Vector3> world)
    {
        var outPts = new List<Vector2>();
        if (world == null || world.Count == 0) return outPts;

        Vector3 u = Vector3.right;   // fallback if no camera
        Vector3 v = Vector3.up;
        if (Camera.main != null)
        {
            u = Camera.main.transform.right;
            v = Camera.main.transform.up;
        }

        Vector3 origin = world[0];
        foreach (var p in world)
        {
            Vector3 d = p - origin;
            outPts.Add(new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v)));
        }
        return outPts;
    }

    // Signed winding of the stroke relative to the camera.
    // Uses the 3D shoelace formula (Newell's method projected onto camera forward).
    // Result:  > 0 → counter-clockwise from viewer,  < 0 → clockwise.
    // Computed from raw world-space points so it is immune to projection instability.
    static float ComputeWindingSign3D(List<Vector3> world)
    {
        if (world == null || world.Count < 3) return 0f;

        Vector3 camFwd = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;

        // Centroid-centre for numerical stability
        Vector3 centroid = Vector3.zero;
        foreach (var p in world) centroid += p;
        centroid /= world.Count;

        float dotSum = 0f;
        for (int i = 0; i < world.Count - 1; i++)
        {
            Vector3 a = world[i]     - centroid;
            Vector3 b = world[i + 1] - centroid;
            dotSum += Vector3.Dot(Vector3.Cross(a, b), camFwd);
        }
        return Mathf.Sign(dotSum);
    }

    // Net start→end direction of the stroke projected onto camera axes.
    // x = horizontal (camera.right), y = vertical (camera.up).
    // Used to resolve swipe_up vs swipe_down after DTW identifies a swipe shape.
    static Vector2 ComputeStrokeDirection(List<Vector3> world)
    {
        if (world == null || world.Count < 2) return Vector2.zero;
        Vector3 dir = world[world.Count - 1] - world[0];
        Vector3 u   = Camera.main != null ? Camera.main.transform.right : Vector3.right;
        Vector3 v   = Camera.main != null ? Camera.main.transform.up    : Vector3.up;
        return new Vector2(Vector3.Dot(dir, u), Vector3.Dot(dir, v));
    }

    // ── Resampling ────────────────────────────────────────────────────────────

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
        if (outPts[outPts.Count - 1] != pts[pts.Count - 1])
            outPts.Add(pts[pts.Count - 1]);
        return outPts;
    }

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
                float   t = (I - D) / d;
                Vector2 q = Vector2.Lerp(pts[idx - 1], pts[idx], t);
                outPts.Add(q);
                pts[idx - 1] = q;
                D = 0f;
            }
            else
            {
                D += d;
                idx++;
                if (idx == pts.Count && outPts.Count < N)
                    while (outPts.Count < N) outPts.Add(pts[pts.Count - 1]);
            }
        }
        while (outPts.Count < N) outPts.Add(pts[pts.Count - 1]);
        if (outPts.Count > N) outPts.RemoveRange(N, outPts.Count - N);
        return outPts;
    }

    // ── Normalisation ─────────────────────────────────────────────────────────

    List<Vector2> NormalizeSequence(List<Vector2> seq)
    {
        var outSeq = new List<Vector2>(seq.Count);
        Vector2 centroid = Vector2.zero;
        foreach (var p in seq) centroid += p;
        centroid /= seq.Count;

        float minx = float.MaxValue, maxx = float.MinValue;
        float miny = float.MaxValue, maxy = float.MinValue;
        foreach (var p in seq)
        {
            if (p.x < minx) minx = p.x;  if (p.x > maxx) maxx = p.x;
            if (p.y < miny) miny = p.y;  if (p.y > maxy) maxy = p.y;
        }
        float scale = Mathf.Max(maxx - minx, maxy - miny);
        if (scale < 1e-6f) scale = 1f;

        foreach (var p in seq)
            outSeq.Add((p - centroid) / scale);

        return outSeq;
    }

    // ── DTW ───────────────────────────────────────────────────────────────────

    float DTWDistance(float[] s1, float[] s2, int N, int window)
    {
        float INF = 1e9f;
        var dtw = new float[N + 1, N + 1];
        for (int i = 0; i <= N; i++)
            for (int j = 0; j <= N; j++)
                dtw[i, j] = INF;
        dtw[0, 0] = 0f;

        for (int i = 1; i <= N; ++i)
        {
            int start = Math.Max(1, i - window);
            int end   = Math.Min(N, i + window);
            for (int j = start; j <= end; ++j)
            {
                float cost    = Euclidean2D(s1, s2, i - 1, j - 1);
                float minPrev = Mathf.Min(dtw[i - 1, j], Mathf.Min(dtw[i, j - 1], dtw[i - 1, j - 1]));
                dtw[i, j] = cost + minPrev;
            }
        }
        return dtw[N, N] / N;
    }

    static float Euclidean2D(float[] s1, float[] s2, int i, int j)
    {
        float dx = s1[2 * i]     - s2[2 * j];
        float dy = s1[2 * i + 1] - s2[2 * j + 1];
        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}
