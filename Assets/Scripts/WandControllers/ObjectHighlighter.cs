using System.Collections.Generic;
using UnityEngine;

public class ObjectHighlighter : MonoBehaviour
{
    [SerializeField] List<MeshRenderer> meshesToHighlight;
    [SerializeField] Material outlineMat;
    [SerializeField] Material fresnelMaterial;

    // Store original materials
    private Dictionary<MeshRenderer, Material[]> originalMats = new Dictionary<MeshRenderer, Material[]>();

    // Store generated fresnel materials (so we can clean up if needed)
    private Dictionary<MeshRenderer, Material[]> fresnelMatsCache = new Dictionary<MeshRenderer, Material[]>();

    private bool isHighlighted = false;

    void Awake()
    {
        foreach (var mesh in meshesToHighlight)
        {
            originalMats[mesh] = mesh.materials;
        }
    }

    public void HighlightMesh(bool highlight)
    {
        if (highlight == isHighlighted) return;
        isHighlighted = highlight;

        foreach (var mesh in meshesToHighlight)
        {
            if (highlight)
                AddHighlight(mesh);
            else
                RemoveHighlight(mesh);
        }
    }
    void AddHighlight(MeshRenderer mesh)
    {
        if (!originalMats.ContainsKey(mesh)) return;

        var original = originalMats[mesh];

        List<Material> newMats = new List<Material>();
        Material[] fresnelMats = new Material[original.Length];

        for (int i = 0; i < original.Length; i++)
        {
            Material origMat = original[i];
            Material fresnelInstance = new Material(fresnelMaterial);

            // =========================
            // TEXTURE COPY (ROBUST)
            // =========================

            Texture tex = null;
            Vector2 scale = Vector2.one;
            Vector2 offset = Vector2.zero;

            string[] textureProps =
            {
            "_BaseMap",          // URP
            "baseColorTexture",     // glTF (MOST IMPORTANT)
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
                        scale = origMat.GetTextureScale(prop);
                        offset = origMat.GetTextureOffset(prop);
                        break;
                    }
                }
            }

            // APPLY TEXTURE TO FRESNEL
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

            // =========================
            // COLOR COPY (ALWAYS)
            // =========================

            Color col = Color.white;

            string[] colorProps =
            {
            "_BaseColor",        // URP
            "_Color",            // Standard
            "baseColorFactor"   // glTF
        };

            foreach (var prop in colorProps)
            {
                if (origMat.HasProperty(prop))
                {
                    col = origMat.GetColor(prop);
                    break;
                }
            }

            // APPLY COLOR TO FRESNEL
            if (fresnelInstance.HasProperty("_BaseColor"))
                fresnelInstance.SetColor("_BaseColor", col);

            if (fresnelInstance.HasProperty("_Color"))
                fresnelInstance.SetColor("_Color", col);

            // =========================
            fresnelMats[i] = fresnelInstance;
            newMats.Add(fresnelInstance);
        }

        // Cache for cleanup
        fresnelMatsCache[mesh] = fresnelMats;

        // Add outline material at end
        newMats.Add(outlineMat);

        mesh.materials = newMats.ToArray();
    }

    void Update()
    {
        if (!isHighlighted) return;
    }

    void RemoveHighlight(MeshRenderer mesh)
    {
        if (!originalMats.ContainsKey(mesh)) return;

        mesh.materials = originalMats[mesh];

        // Optional: cleanup generated materials
        if (fresnelMatsCache.ContainsKey(mesh))
        {
            foreach (var mat in fresnelMatsCache[mesh])
            {
                if (mat != null)
                    Destroy(mat);
            }

            fresnelMatsCache.Remove(mesh);
        }
    }
}