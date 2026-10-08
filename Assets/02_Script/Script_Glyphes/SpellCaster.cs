using UnityEngine;
using UnityEngine.Events;

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

    [Header("Verrou (uniquement pour le sort de fin)")]
    [SerializeField] private SymbolesLivre m_book;              // vide = pas de verrou, le sort marche toujours

    [Header("Après le sort")]
    [SerializeField] private UnityEvent m_onCast;               // appelé juste après l'apparition de l'effet (ex : FinDuJeu.Lancer pour le sort de fin)

    // Appelée par l'événement m_onSpell de GlyphReader.
    // Doit être public et avoir exactement ces paramètres (string, Vector3) pour apparaître dans l'Inspector.
    public void CastSpell(string glyph, Vector3 position)
    {
        // --- 1. --- Le bon glyphe ?
        if (glyph != m_glyphName) return;   // pas le bon glyphe : on ignore

        // --- 2. --- Le sort est-il débloqué ?
        // Si un livre est relié, le sort ne marche qu'une fois toutes les pages validées.
        if (m_book != null && !m_book.EstResolu) return;

        // --- 3. --- Où faire apparaître l'effet
        Vector3 spawnPos = position + m_offset;   // par défaut : sur la feuille

        if (m_castOnFloor)
        {
            // On prend la position du joueur (X et Z de la tête)
            // et on remplace la hauteur par celle du sol.
            spawnPos = m_head.position;
            spawnPos.y = m_trackingSpace.position.y;
        }

        // --- 4. --- Lancer l'effet
        GameObject fx = Instantiate(m_effectPrefab, spawnPos, Quaternion.identity);
        Destroy(fx, m_lifetime);            // nettoyage automatique

        // --- 5. --- Prévenir les autres scripts (rien n'est branché par défaut)
        m_onCast.Invoke();
    }
}