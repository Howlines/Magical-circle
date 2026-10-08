using UnityEngine;

// Reçoit les sorts envoyés par GlyphReader et fait apparaître l'effet.
// Un composant SpellCaster par glyphe.
public class SpellCaster : MonoBehaviour
{
    [SerializeField] private string m_glyphName = "water";      // nom du glyphe, tel qu'affiché dans "Vu : ..."
    [SerializeField] private GameObject m_effectPrefab;         // l'effet à faire apparaître
    [SerializeField] private float m_lifetime = 5f;             // secondes avant de supprimer l'effet
    [SerializeField] private Vector3 m_offset = new Vector3(0f, 0.02f, 0f);   // 2 cm au-dessus de la feuille

    [Header("Sort au sol (uniquement pour le sort de fin)")]
    [SerializeField] private bool m_castOnFloor = false;        // coché = apparaît au sol sous le joueur
    [SerializeField] private Transform m_head;                  // CenterEyeAnchor
    [SerializeField] private Transform m_trackingSpace;         // TrackingSpace de l'OVRCameraRig (hauteur du sol)

    // Appelée par l'événement m_onSpell de GlyphReader.
    // Doit être public et avoir exactement ces paramètres (string, Vector3) pour apparaître dans l'Inspector.
    public void CastSpell(string glyph, Vector3 position)
    {
        if (glyph != m_glyphName) return;   // pas le bon glyphe : on ignore

        Vector3 spawnPos = position + m_offset;   // par défaut : sur la feuille

        if (m_castOnFloor)
        {
            // On prend la position du joueur (X et Z de la tête)
            // et on remplace la hauteur par celle du sol.
            spawnPos = m_head.position;
            spawnPos.y = m_trackingSpace.position.y;
        }

        GameObject fx = Instantiate(m_effectPrefab, spawnPos, Quaternion.identity);
        Destroy(fx, m_lifetime);            // nettoyage automatique
    }
}