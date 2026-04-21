using System.Collections.Generic;
using UnityEngine;

public class ObjectHighlighter : MonoBehaviour
{
    [SerializeField] List<MeshRenderer>        meshesToHighlight;
    [SerializeField] List<SkinnedMeshRenderer> skinnedMeshesToHighlight;
    [SerializeField] Material outlineMat;
    [SerializeField] Material fresnelMaterial;

    // Unified renderer list — populated in Awake from whichever list is filled.
    // Both MeshRenderer and SkinnedMeshRenderer inherit from Renderer,
    // so .materials works identically on both.
    private List<Renderer> _renderers = new List<Renderer>();

    private Dictionary<Renderer, Material[]> originalMats    = new Dictionary<Renderer, Material[]>();
    private Dictionary<Renderer, Material[]> fresnelMatsCache = new Dictionary<Renderer, Material[]>();

    private bool isHighlighted = false;

    void Awake()
    {
        // Prefer MeshRenderer list if populated; fall back to SkinnedMeshRenderer.
        if (meshesToHighlight != null && meshesToHighlight.Count > 0)
        {
            foreach (var mr in meshesToHighlight)
                if (mr != null) _renderers.Add(mr);
        }
        else if (skinnedMeshesToHighlight != null && skinnedMeshesToHighlight.Count > 0)
        {
            foreach (var smr in skinnedMeshesToHighlight)
                if (smr != null) _renderers.Add(smr);
        }
        else
        {
            // Auto-fill: grab all child renderers of either type
            foreach (var mr  in GetComponentsInChildren<MeshRenderer>(true))
                _renderers.Add(mr);

            // Only fall through to skinned if nothing was found above
            if (_renderers.Count == 0)
                foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    _renderers.Add(smr);
        }

        foreach (var r in _renderers)
            originalMats[r] = r.materials;
    }

    public void HighlightMesh(bool highlight)
    {
        if (highlight == isHighlighted) return;
        isHighlighted = highlight;

        foreach (var r in _renderers)
        {
            if (highlight) AddHighlight(r);
            else           RemoveHighlight(r);
        }
    }

    void AddHighlight(Renderer r)
    {
        if (!originalMats.ContainsKey(r)) return;

        // Re-capture current materials so freeze/other runtime swaps are respected.
        // Destroy any stale fresnel instances from a previous AddHighlight.
        if (fresnelMatsCache.ContainsKey(r))
        {
            foreach (var mat in fresnelMatsCache[r])
                if (mat != null) Destroy(mat);
            fresnelMatsCache.Remove(r);
        }
        originalMats[r] = r.materials;

        var original = originalMats[r];

        List<Material> newMats    = new List<Material>();
        Material[]     fresnelMats = new Material[original.Length];

        for (int i = 0; i < original.Length; i++)
        {
            Material origMat        = original[i];
            Material fresnelInstance = new Material(fresnelMaterial);

            // ── Texture copy ─────────────────────────────────────────────────
            Texture tex    = null;
            Vector2 scale  = Vector2.one;
            Vector2 offset = Vector2.zero;

            string[] textureProps =
            {
                "_BaseMap",          // URP
                "baseColorTexture",  // glTF
                "_MainTex",          // Standard
                "_BaseColorTexture"  // some glTF importers
            };

            foreach (var prop in textureProps)
            {
                if (origMat.HasProperty(prop))
                {
                    tex = origMat.GetTexture(prop);
                    if (tex != null)
                    {
                        scale  = origMat.GetTextureScale(prop);
                        offset = origMat.GetTextureOffset(prop);
                        break;
                    }
                }
            }

            if (tex != null)
            {
                if (fresnelInstance.HasProperty("_BaseMap"))
                {
                    fresnelInstance.SetTexture("_BaseMap", tex);
                    fresnelInstance.SetTextureScale("_BaseMap", scale);
                    fresnelInstance.SetTextureOffset("_BaseMap", offset);
                }
                if (fresnelInstance.HasProperty("_MainTex"))
                {
                    fresnelInstance.SetTexture("_MainTex", tex);
                    fresnelInstance.SetTextureScale("_MainTex", scale);
                    fresnelInstance.SetTextureOffset("_MainTex", offset);
                }
            }

            // ── Colour copy ──────────────────────────────────────────────────
            Color col = Color.white;

            string[] colorProps =
            {
                "_BaseColor",    // URP
                "_Color",        // Standard
                "baseColorFactor" // glTF
            };

            foreach (var prop in colorProps)
            {
                if (origMat.HasProperty(prop))
                {
                    col = origMat.GetColor(prop);
                    break;
                }
            }

            if (fresnelInstance.HasProperty("_BaseColor")) fresnelInstance.SetColor("_BaseColor", col);
            if (fresnelInstance.HasProperty("_Color"))     fresnelInstance.SetColor("_Color",     col);

            fresnelMats[i] = fresnelInstance;
            newMats.Add(fresnelInstance);
        }

        fresnelMatsCache[r] = fresnelMats;
        newMats.Add(outlineMat);
        r.materials = newMats.ToArray();
    }

    void RemoveHighlight(Renderer r)
    {
        if (!originalMats.ContainsKey(r)) return;

        r.materials = originalMats[r];

        if (fresnelMatsCache.ContainsKey(r))
        {
            foreach (var mat in fresnelMatsCache[r])
                if (mat != null) Destroy(mat);
            fresnelMatsCache.Remove(r);
        }
    }

    /// Call this after any external material change (e.g. freeze ending) while
    /// the object is still highlighted.  Re-applies the highlight on top of
    /// the current materials so the internal cache doesn't hold stale data.
    public void RefreshHighlight()
    {
        if (!isHighlighted) return;

        foreach (var r in _renderers)
            if (r != null) AddHighlight(r);
    }

    void Update()
    {
        if (!isHighlighted) return;
    }
}
