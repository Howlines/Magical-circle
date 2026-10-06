using System.Collections.Generic;
using System.Threading.Tasks;
using MagicCircle;
using Meta.XR;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Lit les glyphes dans l'image de la caméra du casque, et lance les sorts.
// Le script est allumé/éteint par le geste (deux paumes vers le haut).
public class GlyphReader : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private PassthroughCameraAccess m_cameraAccess;
    [SerializeField] private Text m_text;
    [SerializeField] private Texture2D[] m_refImages;   // photos de référence (Read/Write coché)
    [SerializeField] private Transform m_table;         // objet posé sur la table, axe vert vers le haut

    [Header("Réglages")]
    [SerializeField] private GlyphRecognizer m_recognizer = new GlyphRecognizer();
    [SerializeField] private float m_interval = 0.3f;   // secondes entre deux analyses
    [SerializeField, Range(0.2f, 1f)] private float m_zone = 0.6f;   // part centrale de l'image caméra analysée
    [SerializeField] private int m_maxSize = 800;       // taille max de l'image analysée
    [SerializeField] private int m_historySize = 5;     // nombre de dernières analyses mémorisées
    [SerializeField] private int m_votesNeeded = 3;     // combien doivent être d'accord pour valider

    [Header("Événements")]
    [SerializeField] private UnityEvent<string, Vector3> m_onSpell;   // glyphe reconnu + position du cercle
    [SerializeField] private UnityEvent<Vector3> m_onFizzle;          // cercle sans glyphe reconnu (fumée)

    private Task<GlyphResult> m_task;   // analyse en cours
    private float m_timer;
    private readonly Queue<string> m_history = new Queue<string>();
    private readonly Queue<string> m_messages = new Queue<string>();   // derniers messages, pour le debug
    private string m_current = "rien";  // dernier résultat validé
    private bool m_discard;             // NOUVEAU : vrai = ignorer le résultat de l'analyse en cours

    // MODIFIÉ : Awake au lieu de Start.
    // Awake s'exécute au chargement de la scène, même si le script est désactivé,
    // donc les références sont prêtes avant le premier geste (pas de saccade).
    private void Awake()
    {
        foreach (Texture2D tex in m_refImages)
        {
            byte[] gray = ToGray(tex.GetPixels32(), tex.width, tex.height, 1f, out int w, out int h);
            if (!m_recognizer.AddReference(tex.name.ToLower(), gray, w, h, out string error))
                Debug.LogWarning("Référence " + tex.name + " ignorée : " + error);
        }
    }

    // NOUVEAU : appelé à chaque fois que le geste allume le script.
    private void OnEnable()
    {
        m_history.Clear();          // on oublie les anciens votes
        m_current = "rien";         // on peut relancer le même sort qu'avant
        m_timer = 0f;               // on analyse tout de suite
        m_discard = m_task != null; // une vieille analyse tourne encore ? on ignorera son résultat
    }

    // NOUVEAU : appelé à chaque fois que le joueur relâche le geste.
    private void OnDisable()
    {
        if (m_text != null) m_text.text = "En attente du geste";
    }

    private void Update()
    {
        if (!m_cameraAccess.IsPlaying)
        {
            m_text.text = "Caméra pas prête";
            return;
        }

        // --- 1. On attend la fin de l'analyse en cours ---
        if (m_task != null)
        {
            if (!m_task.IsCompleted) return;
            if (m_task.IsFaulted) Debug.LogError(m_task.Exception);
            else if (!m_discard) OnResult(m_task.Result);   // MODIFIÉ : on jette les vieux résultats
            m_task = null;
            m_discard = false;                              // NOUVEAU
        }

        m_timer -= Time.deltaTime;
        if (m_timer > 0f) return;
        m_timer = m_interval;

        // --- 2. Le centre de l'image de la caméra, en gris ---
        // On garde seulement le centre (là où on regarde la feuille), mais sans le réduire :
        // le cercle garde tous ses détails, même vu de biais.
        Vector2Int res = m_cameraAccess.CurrentResolution;
        byte[] gray = ToGray(m_cameraAccess.GetColors().ToArray(), res.x, res.y, m_zone, out int w, out int h);

        // --- 3. L'analyse, sur un autre thread pour ne pas faire saccader le casque ---
        m_task = Task.Run(() => m_recognizer.Analyse(gray, w, h));
    }

    private void OnResult(GlyphResult res)
    {
        // --- 4. Ce qu'on voit sur cette image ---
        string seen = res.glyph != null ? res.glyph : res.circleFound ? "inconnu" : "rien";

        // --- 5. Vote : il faut le même résultat sur plusieurs des dernières analyses ---
        m_history.Enqueue(seen);
        if (m_history.Count > m_historySize) m_history.Dequeue();
        int votes = 0;
        foreach (string s in m_history) if (s == seen) votes++;

        // Les 4 derniers messages restent affichés, pour avoir le temps de les lire
        m_messages.Enqueue(res.message);
        if (m_messages.Count > 4) m_messages.Dequeue();
        m_text.text = $"Vu : {seen} ({votes}/{m_history.Count})\nValidé : {m_current}\n" + string.Join("\n", m_messages);

        // On n'agit que quand le résultat validé change (sinon le sort se relancerait en boucle)
        if (votes < m_votesNeeded || seen == m_current) return;
        m_current = seen;
        if (seen == "rien") return;

        // --- 6. Le sort, à l'endroit du cercle ---
        Vector3 pos = CirclePosition(res);
        if (seen == "inconnu") m_onFizzle.Invoke(pos);
        else m_onSpell.Invoke(seen, pos);
    }

    // Le centre du cercle dans l'image devient un rayon qui part de la caméra.
    // Là où il touche la table, c'est le cercle.
    private Vector3 CirclePosition(GlyphResult res)
    {
        // Position dans l'image analysée (de 0 à 1), puis dans l'image complète de la caméra
        float border = (1f - m_zone) / 2f;
        Vector2 point = new Vector2(border + res.cx / res.w * m_zone, border + res.cy / res.h * m_zone);
        Ray ray = m_cameraAccess.ViewportPointToRay(point);
        Plane table = new Plane(m_table.up, m_table.position);
        return table.Raycast(ray, out float dist) ? ray.GetPoint(dist) : ray.GetPoint(0.5f);
    }

    // Garde le centre de l'image (zone = 1 : toute l'image), le réduit à m_maxSize et le passe en gris.
    // Chaque pixel de sortie = moyenne des pixels qu'il recouvre (pour ne pas perdre les traits fins).
    private byte[] ToGray(Color32[] pixels, int fullW, int fullH, float zone, out int w, out int h)
    {
        int sw = (int)(fullW * zone), sh = (int)(fullH * zone);           // taille de la zone
        int offX = (fullW - sw) / 2, offY = (fullH - sh) / 2;             // son coin en bas à gauche
        float scale = Mathf.Min(1f, (float)m_maxSize / Mathf.Max(sw, sh));
        w = Mathf.Max(1, Mathf.RoundToInt(sw * scale));
        h = Mathf.Max(1, Mathf.RoundToInt(sh * scale));

        byte[] gray = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int y0 = y * sh / h, y1 = Mathf.Max(y0 + 1, (y + 1) * sh / h);
            for (int x = 0; x < w; x++)
            {
                int x0 = x * sw / w, x1 = Mathf.Max(x0 + 1, (x + 1) * sw / w);
                int somme = 0, nb = 0;
                for (int yy = y0; yy < y1; yy++)
                {
                    for (int xx = x0; xx < x1; xx++)
                    {
                        Color32 p = pixels[(offY + yy) * fullW + offX + xx];
                        somme += (p.r * 77 + p.g * 150 + p.b * 29) >> 8;
                        nb++;
                    }
                }
                gray[y * w + x] = (byte)(somme / nb);
            }
        }
        return gray;
    }
}