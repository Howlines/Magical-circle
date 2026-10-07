using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cercle magique décoratif : tourne lentement et "respire" (sa lumière pulse doucement).
/// </summary>
public class CercleMagique : MonoBehaviour
{
    [Tooltip("Degrés par seconde. Négatif = sens des aiguilles d'une montre.")]
    public float vitesse = -6f;
    [Tooltip("Image dont l'opacité pulse (laisser vide pour ne pas pulser).")]
    public Graphic pulsation;
    public float opaciteMin = 0.55f;
    public float opaciteMax = 0.95f;
    public float dureePulsation = 3.5f;

    void Update()
    {
        transform.Rotate(0f, 0f, vitesse * Time.unscaledDeltaTime);

        if (pulsation != null)
        {
            var t = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / dureePulsation) + 1f) * 0.5f;
            var c = pulsation.color;
            c.a = Mathf.Lerp(opaciteMin, opaciteMax, t);
            pulsation.color = c;
        }
    }
}
