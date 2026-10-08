using UnityEngine;

// Fait apparaître un effet quand le sort rate.
// C'est GlyphReader qui décide QUAND (plusieurs échecs d'affilée) : ici on joue juste l'effet.
// Un seul composant suffit pour toute la scène.
public class FizzleCaster : MonoBehaviour
{
    [SerializeField] private GameObject m_effectPrefab;         // l'effet de raté (fumée, étincelles...)
    [SerializeField] private float m_lifetime = 2f;             // secondes avant de supprimer l'effet
    [SerializeField] private Vector3 m_offset = new Vector3(0f, 0.02f, 0f);   // 2 cm au-dessus de la feuille

    [Header("Son (optionnel)")]
    [SerializeField] private AudioClip m_sound;                 // vide = pas de son
    [SerializeField, Range(0f, 1f)] private float m_volume = 1f;

    // Appelée par l'événement m_onFizzle de GlyphReader.
    // Doit être public et avoir exactement ce paramètre (Vector3) pour apparaître dans l'Inspector.
    public void Fizzle(Vector3 position)
    {
        // --- 1. --- Où faire apparaître l'effet : sur la feuille
        Vector3 spawnPos = position + m_offset;

        // --- 2. --- Lancer l'effet
        if (m_effectPrefab != null)
        {
            GameObject fx = Instantiate(m_effectPrefab, spawnPos, Quaternion.identity);
            Destroy(fx, m_lifetime);        // nettoyage automatique
        }

        // --- 3. --- Jouer le son, à l'endroit de la feuille
        if (m_sound != null)
            AudioSource.PlayClipAtPoint(m_sound, spawnPos, m_volume);
    }
}