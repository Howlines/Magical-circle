using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Effet de survol d'un bouton du menu : halo doré qui s'allume, léger grossissement,
/// glyphe qui tourne doucement. Marche avec le doigt et le rayon des mains (événements UI du Meta SDK).
/// </summary>
public class BoutonMagique : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public Graphic lueur;
    public RectTransform glyphe;
    public float grossissement = 1.06f;
    public float vitesseGlyphe = 60f;

    bool m_Survol;
    bool m_Appui;
    float m_Intensite;

    void OnDisable()
    {
        m_Survol = m_Appui = false;
        m_Intensite = 0f;
        Appliquer();
    }

    void Update()
    {
        var cible = m_Appui ? 1f : m_Survol ? 0.75f : 0f;
        m_Intensite = Mathf.MoveTowards(m_Intensite, cible, Time.unscaledDeltaTime * 5f);
        Appliquer();

        if (glyphe != null && m_Intensite > 0.01f)
            glyphe.Rotate(0f, 0f, -vitesseGlyphe * m_Intensite * Time.unscaledDeltaTime);
    }

    void Appliquer()
    {
        if (lueur != null)
        {
            var c = lueur.color;
            c.a = m_Intensite;
            lueur.color = c;
        }
        transform.localScale = Vector3.one * Mathf.Lerp(1f, grossissement, m_Intensite);
    }

    public void OnPointerEnter(PointerEventData e) => m_Survol = true;
    public void OnPointerExit(PointerEventData e) => m_Survol = m_Appui = false;
    public void OnPointerDown(PointerEventData e) => m_Appui = true;
    public void OnPointerUp(PointerEventData e) => m_Appui = false;
}
