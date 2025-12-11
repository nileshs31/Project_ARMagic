using UnityEngine;
using System.Collections.Generic;

public class GestureRecognizer : MonoBehaviour
{
    public struct Gesture
    {
        public string Name;
        public Vector2[] Points;
    }

    private List<Gesture> templates = new List<Gesture>();
    private const int N = 32;

    void Awake()
    {
        // 1. Initialize templates 
        // We Normalize them immediately.
        // FIX: I added the closing points to the Square/Triangle so they look like drawn shapes.
        templates.Add(new Gesture { Name = "Circle", Points = Normalize(GenerateCirclePoints()) });
        templates.Add(new Gesture { Name = "Square", Points = Normalize(GenerateSquarePoints()) });
        templates.Add(new Gesture { Name = "Triangle", Points = Normalize(GenerateTrianglePoints()) });
    }

    public string Recognize(List<Vector3> worldPoints, out bool isClockwise)
    {
        if (worldPoints.Count < 5)
        {
            isClockwise = false;
            return "Too Short";
        }

        // A. Convert 3D World points to 2D
        List<Vector2> projectedPoints = new List<Vector2>();
        Camera cam = Camera.main;

        // Optimize: Use the first point as a reference to keep numbers manageable
        Vector3 origin = worldPoints[0];

        foreach (var pt in worldPoints)
        {
            Vector3 screenPt = cam.WorldToScreenPoint(pt);
            projectedPoints.Add(new Vector2(screenPt.x, screenPt.y));
        }

        isClockwise = IsClockwise(projectedPoints);

        Vector2[] normalizedInput = Normalize(projectedPoints.ToArray());

        float bestScore = float.MaxValue;
        string bestMatch = "Unknown";

        // DEBUG: Print scores to see what's happening
        string debugLog = "Scores: ";

        foreach (var template in templates)
        {
            float dist = GreedyCloudMatch(normalizedInput, template.Points);
            debugLog += $"{template.Name}:{dist:F2} | ";

            if (dist < bestScore)
            {
                bestScore = dist;
                bestMatch = template.Name;
            }
        }

        Debug.Log(debugLog); // Check your console for this!

        // Adjusted Threshold: 2.5 is usually a safe bet for VR drawing
        if (bestScore > 2.5f) return "Unknown";

        return bestMatch;
    }

    // --- MATH HELPERS ---

    private bool IsClockwise(List<Vector2> points)
    {
        float sum = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 current = points[i];
            Vector2 next = points[(i + 1) % points.Count];
            sum += (next.x - current.x) * (next.y + current.y);
        }
        return sum > 0;
    }

    private float GreedyCloudMatch(Vector2[] points, Vector2[] template)
    {
        if (points.Length != template.Length) return float.MaxValue;

        float e = 0.5f;
        int step = Mathf.FloorToInt(Mathf.Pow(points.Length, 1.0f - e));
        float min = float.MaxValue;

        for (int i = 0; i < points.Length; i += step)
        {
            float dist1 = CloudDistance(points, template, i);
            float dist2 = CloudDistance(template, points, i);
            min = Mathf.Min(min, Mathf.Min(dist1, dist2));
        }
        return min;
    }

    private float CloudDistance(Vector2[] pts1, Vector2[] pts2, int start)
    {
        bool[] matched = new bool[pts2.Length];
        float sum = 0;
        int i = start;
        for (int k = 0; k < pts1.Length; k++)
        {
            int index = -1;
            float min = float.MaxValue;
            for (int j = 0; j < matched.Length; j++)
            {
                if (!matched[j])
                {
                    float d = Vector2.Distance(pts1[i], pts2[j]);
                    if (d < min) { min = d; index = j; }
                }
            }
            if (index == -1) break;
            matched[index] = true;
            sum += min;
            i = (i + 1) % pts1.Length;
        }
        return sum;
    }

    private Vector2[] Normalize(Vector2[] points)
    {
        points = Resample(points, N);

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in points)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }
        float scale = Mathf.Max(maxX - minX, maxY - minY);
        if (scale == 0) scale = 1;

        Vector2 center = new Vector2((minX + maxX) / 2.0f, (minY + maxY) / 2.0f);
        Vector2[] newPoints = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            newPoints[i] = (points[i] - center) / scale;
        }
        return newPoints;
    }

    // --- FIX: ROBUST RESAMPLER ---
    // This correctly handles low-poly inputs (like 4-point squares)
    private Vector2[] Resample(Vector2[] points, int n)
    {
        float I = PathLength(points) / (n - 1);
        float D = 0;

        List<Vector2> newPoints = new List<Vector2>();
        newPoints.Add(points[0]);

        for (int i = 1; i < points.Length; i++)
        {
            float d = Vector2.Distance(points[i - 1], points[i]);

            if (D + d >= I)
            {
                float qx = points[i - 1].x + ((I - D) / d) * (points[i].x - points[i - 1].x);
                float qy = points[i - 1].y + ((I - D) / d) * (points[i].y - points[i - 1].y);
                Vector2 q = new Vector2(qx, qy);

                newPoints.Add(q);

                // Magic: Insert q back into the list so the next iteration starts from q
                // But since we can't easily insert into C# arrays, we adjust our reference
                points[i - 1] = q;
                D = 0;
                i--; // Backtrack to process the remaining distance on this segment
            }
            else
            {
                D += d;
            }
        }

        // Safety: ensure exactly N points
        if (newPoints.Count == n - 1) newPoints.Add(points[points.Length - 1]);
        while (newPoints.Count < n) newPoints.Add(newPoints[newPoints.Count - 1]);
        if (newPoints.Count > n) newPoints.RemoveRange(n, newPoints.Count - n);

        return newPoints.ToArray();
    }

    private float PathLength(Vector2[] points)
    {
        float d = 0;
        for (int i = 1; i < points.Length; i++) d += Vector2.Distance(points[i - 1], points[i]);
        return d;
    }

    // --- TEMPLATES (FIXED) ---
    Vector2[] GenerateCirclePoints()
    {
        Vector2[] p = new Vector2[32];
        for (int i = 0; i < 32; i++)
        {
            float angle = (i / 32.0f) * Mathf.PI * 2;
            p[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
        return p;
    }

    Vector2[] GenerateSquarePoints()
    {
        // FIX: Added the 5th point to close the loop (0,0)
        return new Vector2[] {
            new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0), new Vector2(0,0)
        };
    }

    Vector2[] GenerateTrianglePoints()
    {
        // FIX: Added the 4th point to close the loop (0,0)
        return new Vector2[] {
            new Vector2(0,0), new Vector2(0.5f, 1), new Vector2(1,0), new Vector2(0,0)
        };
    }
}