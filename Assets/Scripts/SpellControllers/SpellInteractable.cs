using UnityEngine;

public class SpellInteractable : MonoBehaviour
{
    public void ApplySpell(string spell)
    {
        switch (spell)
        {
            case "circle_cw":
               // 
                break;

            case "triangle":
               // 
                break;

            case "square":
               // 
                break;

            default:
                Debug.Log("Unknown spell on object");
                break;
        }
    }
}