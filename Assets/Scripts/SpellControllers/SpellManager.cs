using UnityEngine;

public class SpellManager : MonoBehaviour
{
    void OnEnable()
    {
        SpellEvents.OnSpellCast += HandleSpell;
    }

    void OnDisable()
    {
        SpellEvents.OnSpellCast -= HandleSpell;
    }

    void HandleSpell(string spell, GameObject target)
    {
        if (target == null)
        {
            CastInAir(spell);
            return;
        }

        var interactable = target.GetComponent<SpellInteractable>();
        if (interactable != null)
        {
            interactable.ApplySpell(spell);
        }
    }

    void CastInAir(string spell)
    {
        Debug.Log($"Spell {spell} cast into air");
        // later  projectile, VFX, etc.
    }
}