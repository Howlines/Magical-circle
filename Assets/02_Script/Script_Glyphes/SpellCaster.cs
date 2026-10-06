using UnityEngine;

// Reçoit les sorts envoyés par GlyphReader et fait apparaître l'effet.
// Pour l'instant : un seul glyphe géré (l'eau).
public class SpellCaster : MonoBehaviour
{
    [SerializeField] private string m_glyphName = "water";      // nom du glyphe, tel qu'affiché dans "Vu : ..."
    [SerializeField] private GameObject m_effectPrefab;         // l'effet à faire apparaître
    [SerializeField] private float m_lifetime = 5f;             // secondes avant de supprimer l'effet
    [SerializeField] private Vector3 m_offset = new Vector3(0f, 0.02f, 0f);   // 2 cm au-dessus de la feuille

    // Appelée par l'événement m_onSpell de GlyphReader.
    // Doit être public et avoir exactement ces paramètres (string, Vector3) pour apparaître dans l'Inspector.
    public void CastSpell(string glyph, Vector3 position)
    {
        if (glyph != m_glyphName) return;   // pas l'eau : on ignore

        GameObject fx = Instantiate(m_effectPrefab, position + m_offset, Quaternion.identity);
        Destroy(fx, m_lifetime);            // nettoyage automatique
    }
}