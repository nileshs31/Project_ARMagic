using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class ShapeRecognizer
{
    public enum ShapeType { Unknown, Circle, Triangle, Square, Quad, Line, Spiral }

    public class Result
    {
        public ShapeType Shape = ShapeType.Unknown;
        public bool Clockwise = false;
        public float Revolutions = 0f;  // for circle/spiral
        public float Radius = 0f;       // mean radius for circle
        public int Corners = 0;         // for polygons
        public float Confidence = 0f;   // 0..1 heuristic
    }

    // Public analyze entry. stroke: world-space Vector3 points (recorded while holding)
    public static Result Analyze(List<Vector3> stroke)
    {
        var r = new Result();
        if (stroke == null || stroke.Count < 6)
        {
            r.Shape = ShapeType.Unknown;
            r.Confidence = 0f;
            return r;
        }

        // 1) Preprocess: downsample by distance, optionally smooth
        var sampled = SampleByDistance(stroke, minDist: 0.004f, maxPoints: 256);
        if (sampled.Count < 6)
        {
            r.Shape = ShapeType.Unknown;
            return r;
        }
        sampled = ChaikinSmooth(sampled, iterations: 1);

        // 2) Project to plane: estimate normal and build 2D basis
        Vector3 normal = EstimateNormal(sampled);
        if (normal.sqrMagnitude < 1e-8f) normal = Vector3.up;
        normal.Normalize();
        Vector3 u = (sampled.Last() - sampled[0]).normalized;
        if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(normal, Vector3.up);
        u = Vector3.ProjectOnPlane(u, normal).normalized;
        Vector3 v = Vector3.Cross(normal, u).normalized;
        Vector2[] pts2 = new Vector2[sampled.Count];
        Vector3 origin = sampled[0]; // local origin
        for (int i = 0; i < sampled.Count; i++)
        {
            Vector3 d = sampled[i] - origin;
            pts2[i] = new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v));
        }

        // compute path scale (used for adaptive thresholds)
        var pts2List = pts2.ToList();
        float scale = PathScale(pts2List);

        // compute signed area/direction on projected (non-normalized) points
        float signedArea = SignedArea(pts2List);
        r.Clockwise = signedArea < 0f;

        // 3) Determine closedness relative to scale
        float startEndDist = Vector2.Distance(pts2[0], pts2[pts2.Length - 1]);
        bool closed = startEndDist <= Mathf.Max(0.18f * scale, 0.03f);

        // ---------- POLYGON FIRST (more robust for triangles/squares) ----------
        if (closed)
        {
            float rdpFactor = 0.12f; // relative epsilon factor (tune this)
            var corners = SimplifyAndGetCorners(pts2List, rdpFactor);
            int cornerCount = corners.Count;
            r.Corners = cornerCount;

            if (cornerCount == 3)
            {
                r.Shape = ShapeType.Triangle;
                r.Confidence = 0.95f;
                return r;
            }
            if (cornerCount == 4)
            {
                // check bounding box aspect ratio for square vs quad
                var simplified = RamerDouglasPeucker(pts2List, Mathf.Clamp(scale * rdpFactor, 0.007f, 0.2f));
                if (simplified.Count >= 3)
                {
                    var poly = simplified;
                    // if last == first (loop) remove for bbox
                    if (poly.Count > 1 && (poly[0] - poly[poly.Count - 1]).magnitude < 1e-5f) poly = poly.Take(poly.Count - 1).ToList();
                    var bb = BoundingBox(poly);
                    float ar = bb.width / Mathf.Max(1e-6f, bb.height);
                    if (ar > 0.7f && ar < 1.4f) r.Shape = ShapeType.Square;
                    else r.Shape = ShapeType.Quad;
                    r.Confidence = 0.9f;
                    return r;
                }
            }
        }

        // ---------- CIRCLE (robust) ----------
        if (DetectCircleRobust(pts2, out Vector2 center, out float meanRadius, out float circScore, out float revolutions, out float signedDelta, out float monotonicityScore, out float spiralCorr))
        {
            // scale-aware thresholds (tweak these if needed)
            float minRevs = 1f;           // near 1 full turn
            float maxCircScore = 0.12f;      // normalized radius stddev
            float minMono = 0.72f;           // angle progression mostly one direction
            bool likelySpiral = Mathf.Abs(spiralCorr) > 0.2f && revolutions > 0.3f;

            bool closedEnough = startEndDist <= Mathf.Max(0.06f, meanRadius * 0.35f);
            bool enoughRevs = revolutions >= minRevs;
            bool circularEnough = circScore <= maxCircScore;
            bool monoEnough = monotonicityScore >= minMono;

            if (closedEnough && enoughRevs && circularEnough && monoEnough && !likelySpiral)
            {
                r.Shape = ShapeType.Circle;
                r.Clockwise = signedDelta < 0f;
                r.Revolutions = revolutions;
                r.Radius = meanRadius;
                r.Confidence = Mathf.Clamp01(1f - circScore);
                return r;
            }
        }

        // ---------- SPIRAL (fallback) ----------
        if (DetectSpiral(pts2, out float spiralRevs, out float spiralScore))
        {
            if (spiralRevs >= 0.6f && spiralScore > 0.25f)
            {
                r.Shape = ShapeType.Spiral;
                r.Revolutions = spiralRevs;
                // direction for spiral: use sign of signedDelta from angle unwrap
                r.Clockwise = ComputeSignedDeltaDirection(pts2) < 0f;
                r.Confidence = 0.6f;
                return r;
            }
        }

        // ---------- FALLBACK: try polygon heuristics again (looser) ----------
        var rdpEps = Mathf.Clamp(scale * 0.06f, 0.007f, 0.2f);
        var simplified2 = RamerDouglasPeucker(pts2List, rdpEps);
        // count corners (angle-based)
        int cornerCount2 = 0;
        for (int i = 1; i < simplified2.Count - 1; i++)
        {
            Vector2 a = simplified2[i - 1];
            Vector2 b = simplified2[i];
            Vector2 c = simplified2[i + 1];
            float ang = Vector2.Angle((a - b).normalized, (c - b).normalized);
            if (ang > 20f) cornerCount2++;
        }
        if (closed && cornerCount2 >= 3 && cornerCount2 <= 6)
        {
            r.Shape = cornerCount2 == 3 ? ShapeType.Triangle : (cornerCount2 == 4 ? ShapeType.Quad : ShapeType.Quad);
            r.Corners = cornerCount2;
            r.Confidence = 0.55f;
            return r;
        }

        // fallback unknown
        r.Shape = ShapeType.Unknown;
        r.Confidence = 0.12f;
        return r;
    }

    // ---------------------------
    // Improved helpers
    // ---------------------------

    static List<Vector3> SampleByDistance(List<Vector3> pts, float minDist = 0.005f, int maxPoints = 300)
    {
        var outPts = new List<Vector3>();
        outPts.Add(pts[0]);
        Vector3 last = pts[0];
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

    static List<Vector3> ChaikinSmooth(List<Vector3> pts, int iterations = 1)
    {
        var cur = pts;
        for (int k = 0; k < iterations; k++)
        {
            var next = new List<Vector3>();
            next.Add(cur[0]);
            for (int i = 0; i < cur.Count - 1; i++)
            {
                Vector3 p0 = cur[i];
                Vector3 p1 = cur[i + 1];
                Vector3 q = Vector3.Lerp(p0, p1, 0.25f);
                Vector3 r = Vector3.Lerp(p0, p1, 0.75f);
                next.Add(q);
                next.Add(r);
            }
            next.Add(cur[cur.Count - 1]);
            cur = next;
        }
        return cur;
    }

    static Vector3 EstimateNormal(List<Vector3> pts)
    {
        Vector3 normal = Vector3.zero;
        for (int i = 2; i < pts.Count; i++)
        {
            Vector3 a = pts[i - 2];
            Vector3 b = pts[i - 1];
            Vector3 c = pts[i];
            Vector3 ab = b - a;
            Vector3 bc = c - b;
            normal += Vector3.Cross(ab, bc);
        }
        return normal;
    }

    static float PathScale(List<Vector2> pts)
    {
        if (pts == null || pts.Count == 0) return 0.01f;
        float minx = float.MaxValue, maxx = float.MinValue, miny = float.MaxValue, maxy = float.MinValue;
        foreach (var p in pts) { if (p.x < minx) minx = p.x; if (p.x > maxx) maxx = p.x; if (p.y < miny) miny = p.y; if (p.y > maxy) maxy = p.y; }
        return Mathf.Max(1e-6f, Mathf.Max(maxx - minx, maxy - miny));
    }

    static (float width, float height) BoundingBox(List<Vector2> pts)
    {
        float minx = float.MaxValue, maxx = float.MinValue, miny = float.MaxValue, maxy = float.MinValue;
        foreach (var p in pts) { if (p.x < minx) minx = p.x; if (p.x > maxx) maxx = p.x; if (p.y < miny) miny = p.y; if (p.y > maxy) maxy = p.y; }
        return (maxx - minx, maxy - miny);
    }

    static float SignedArea(List<Vector2> poly)
    {
        if (poly == null || poly.Count < 3) return 0f;
        float area = 0f;
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % n];
            area += (a.x * b.y - b.x * a.y);
        }
        return area * 0.5f;
    }

    // ---------------------------
    // Robust circle detector (centroid-based + monotonicity + spiral rejection)
    // ---------------------------
    static bool DetectCircleRobust(Vector2[] pts, out Vector2 center, out float meanRadius, out float circScore, out float revolutions, out float signedDelta, out float monotonicityScore, out float spiralCorr)
    {
        center = Vector2.zero; meanRadius = 0f; circScore = 1f; revolutions = 0f; signedDelta = 0f; monotonicityScore = 0f; spiralCorr = 0f;
        int n = pts.Length;
        if (n < 8) return false;

        // centroid as center
        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < n; i++) centroid += pts[i];
        centroid /= n;

        float[] r = new float[n];
        float[] ang = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 d = pts[i] - centroid;
            r[i] = d.magnitude;
            ang[i] = Mathf.Atan2(d.y, d.x);
        }

        // unwrap angles & compute monotonicity
        float last = ang[0];
        float firstUn = last, lastUn = last;
        int posSteps = 0, negSteps = 0;
        int reversals = 0;
        for (int i = 1; i < n; i++)
        {
            float a = ang[i];
            float diff = a - last;
            while (diff > Mathf.PI) { a -= 2f * Mathf.PI; diff = a - last; }
            while (diff < -Mathf.PI) { a += 2f * Mathf.PI; diff = a - last; }
            if (diff > 0) posSteps++; else if (diff < 0) negSteps++;
            if (i > 1)
            {
                float prevDiff = ang[i - 1] - ang[i - 2];
                while (prevDiff > Mathf.PI) prevDiff -= 2f * Mathf.PI;
                while (prevDiff < -Mathf.PI) prevDiff += 2f * Mathf.PI;
                if (prevDiff * diff < 0f && Mathf.Abs(diff) > 0.05f && Mathf.Abs(prevDiff) > 0.05f) reversals++;
            }
            last = a;
            lastUn = a;
        }
        signedDelta = lastUn - firstUn;
        revolutions = Mathf.Abs(signedDelta) / (2f * Mathf.PI);
        monotonicityScore = (float)Mathf.Max(posSteps, negSteps) / Mathf.Max(1, posSteps + negSteps);

        // mean radius & circ score
        float sumR = 0f;
        for (int i = 0; i < n; i++) sumR += r[i];
        meanRadius = sumR / n;
        float var = 0f;
        for (int i = 0; i < n; i++) var += (r[i] - meanRadius) * (r[i] - meanRadius);
        var /= n;
        float std = Mathf.Sqrt(var);
        circScore = meanRadius > 1e-6f ? (std / meanRadius) : 1f;

        // spiral correlation between angle and radius
        float meanA = ang.Average();
        float cov = 0f, varA = 0f, varR = 0f;
        for (int i = 0; i < n; i++)
        {
            float da = ang[i] - meanA;
            cov += da * (r[i] - meanRadius);
            varA += da * da;
            varR += (r[i] - meanRadius) * (r[i] - meanRadius);
        }
        spiralCorr = (varA > 0f && varR > 0f) ? cov / Mathf.Sqrt(varA * varR) : 0f;

        // output
        center = centroid;
        return true;
    }

    // simple unwrap based signed delta used for spiral direction
    static float ComputeSignedDeltaDirection(Vector2[] pts)
    {
        int n = pts.Length;
        if (n < 3) return 0f;
        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < n; i++) centroid += pts[i];
        centroid /= n;
        float last = Mathf.Atan2(pts[0].y - centroid.y, pts[0].x - centroid.x);
        float firstUn = last, lastUn = last;
        for (int i = 1; i < n; i++)
        {
            float a = Mathf.Atan2(pts[i].y - centroid.y, pts[i].x - centroid.x);
            float diff = a - last;
            while (diff > Mathf.PI) { a -= 2f * Mathf.PI; diff = a - last; }
            while (diff < -Mathf.PI) { a += 2f * Mathf.PI; diff = a - last; }
            last = a;
            lastUn = a;
        }
        return lastUn - firstUn;
    }

    // ---------------------------
    // Ramer–Douglas–Peucker (2D)
    // ---------------------------
    public static List<Vector2> RamerDouglasPeucker(List<Vector2> pointList, float epsilon)
    {
        if (pointList == null || pointList.Count < 3) return new List<Vector2>(pointList);
        int index = -1;
        float dmax = 0f;
        for (int i = 1; i < pointList.Count - 1; i++)
        {
            float d = PerpendicularDistance(pointList[i], pointList[0], pointList[pointList.Count - 1]);
            if (d > dmax) { index = i; dmax = d; }
        }

        if (dmax > epsilon)
        {
            var rec1 = RamerDouglasPeucker(pointList.GetRange(0, index + 1), epsilon);
            var rec2 = RamerDouglasPeucker(pointList.GetRange(index, pointList.Count - index), epsilon);
            var result = new List<Vector2>(rec1);
            result.RemoveAt(result.Count - 1);
            result.AddRange(rec2);
            return result;
        }
        else
        {
            return new List<Vector2> { pointList[0], pointList[pointList.Count - 1] };
        }
    }

    static float PerpendicularDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        if (ab.sqrMagnitude < 1e-9f) return (p - a).magnitude;
        float t = Vector2.Dot(p - a, ab) / ab.sqrMagnitude;
        Vector2 proj = a + Mathf.Clamp01(t) * ab;
        return (p - proj).magnitude;
    }

    // ---------------------------
    // Corner extraction / polygon simplification helper
    // ---------------------------
    static List<Vector2> SimplifyAndGetCorners(List<Vector2> pts, float rdpFactor = 0.12f)
    {
        var outList = new List<Vector2>();
        if (pts == null || pts.Count < 3) return outList;

        float scale = PathScale(pts);
        float eps = Mathf.Clamp(scale * rdpFactor, 0.007f, 0.2f);
        var simplified = RamerDouglasPeucker(pts, eps);

        // closed check & ensure loop
        float startEnd = Vector2.Distance(pts[0], pts[pts.Count - 1]);
        bool closed = startEnd <= Mathf.Max(0.18f * scale, 0.03f);
        if (closed && (simplified.Count >= 2) && (Vector2.Distance(simplified[0], simplified[simplified.Count - 1]) > 1e-5f))
            simplified.Add(simplified[0]);

        // now count corners with separation
        var corners = new List<Vector2>();
        float minCornerSeparation = Mathf.Max(0.08f * scale, 0.02f);
        for (int i = 1; i < simplified.Count - 1; i++)
        {
            Vector2 a = simplified[i - 1];
            Vector2 b = simplified[i];
            Vector2 c = simplified[i + 1];
            float ang = Vector2.Angle((a - b).normalized, (c - b).normalized);
            if (ang > 30f)
            {
                if (corners.Count == 0 || Vector2.Distance(corners.Last(), b) >= minCornerSeparation)
                    corners.Add(b);
            }
        }
        return corners;
    }

    // ---------------------------
    // Spiral detection (quick heuristic)
    // ---------------------------
    static bool DetectSpiral(Vector2[] pts, out float revolutions, out float score)
    {
        revolutions = 0f; score = 0f;
        int n = pts.Length;
        if (n < 6) return false;

        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < n; i++) centroid += pts[i];
        centroid /= n;

        float[] r = new float[n];
        float[] ang = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 d = pts[i] - centroid;
            r[i] = d.magnitude;
            ang[i] = Mathf.Atan2(d.y, d.x);
        }

        float last = ang[0], firstUn = last, lastUn = last;
        for (int i = 1; i < n; i++)
        {
            float a = ang[i];
            float diff = a - last;
            while (diff > Mathf.PI) { a -= 2f * Mathf.PI; diff = a - last; }
            while (diff < -Mathf.PI) { a += 2f * Mathf.PI; diff = a - last; }
            last = a; lastUn = a;
        }
        float delta = lastUn - firstUn;
        revolutions = Mathf.Abs(delta) / (2f * Mathf.PI);

        float meanR = r.Average();
        float cov = 0f, varA = 0f, varR = 0f;
        float meanAng = ang.Average();
        for (int i = 0; i < n; i++)
        {
            float a = ang[i];
            float ra = r[i];
            cov += (a - meanAng) * (ra - meanR);
            varA += (a - meanAng) * (a - meanAng);
            varR += (ra - meanR) * (ra - meanR);
        }
        if (varA <= 0 || varR <= 0) { score = 0f; return false; }
        float corr = cov / Mathf.Sqrt(varA * varR);
        score = Mathf.Abs(corr);
        return true;
    }
}
