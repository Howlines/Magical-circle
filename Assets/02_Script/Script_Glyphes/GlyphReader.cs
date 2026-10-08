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
    [SerializeField] private Transform m_aimGuide;      // NOUVEAU : cercle de visée, montre où la caméra regarde sur la table

    [Header("Réglages")]
    [SerializeField] private GlyphRecognizer m_recognizer = new GlyphRecognizer();
    [SerializeField] private float m_interval = 0.3f;   // secondes entre deux analyses
    [SerializeField, Range(0.2f, 1f)] private float m_zone = 0.6f;   // part de l'image caméra analysée
    [SerializeField] private Vector2 m_zoneOffset = Vector2.zero;    // NOUVEAU : décale la zone analysée (en fraction de l'image). y négatif = plus bas
    [SerializeField] private int m_maxSize = 800;       // taille max de l'image analysée
    [SerializeField] private int m_historySize = 5;     // nombre de dernières analyses mémorisées
    [SerializeField] private int m_votesNeeded = 3;     // combien doivent être d'accord pour valider
    [SerializeField] private int m_failsNeeded = 6;     // NOUVEAU : nombre d'"inconnu" D'AFFILÉE avant de rater (environ 0,4 s chacun)

    [Header("Événements")]
    [SerializeField] private UnityEvent<string, Vector3> m_onSpell;   // glyphe reconnu + position du cercle
    [SerializeField] private UnityEvent<Vector3> m_onFizzle;          // cercle sans glyphe reconnu (fumée)

    private Task<GlyphResult> m_task;   // analyse en cours
    private float m_timer;
    private readonly Queue<string> m_history = new Queue<string>();
    private readonly Queue<string> m_messages = new Queue<string>();   // derniers messages, pour le debug
    private string m_current = "rien";  // dernier résultat validé
    private bool m_discard;             // vrai = ignorer le résultat de l'analyse en cours
    private int m_failCount;            // NOUVEAU : nombre d'"inconnu" d'affilée

    // Awake s'exécute au chargement de la scène, même si le composant est décoché
    // (tant que l'objet GlyphReader est actif).
    private void Awake()
    {
        foreach (Texture2D tex in m_refImages)
        {
            byte[] gray = ToGray(tex.GetPixels32(), tex.width, tex.height, 1f, Vector2.zero, out int w, out int h);
            if (!m_recognizer.AddReference(tex.name.ToLower(), gray, w, h, out string error))
                Debug.LogWarning("Référence " + tex.name + " ignorée : " + error);
        }

        // NOUVEAU : on s'éteint nous-mêmes au démarrage. Seul le geste pourra nous rallumer,
        // même si la case du composant a été laissée cochée par erreur dans l'Inspector.
        enabled = false;

        // On écrit le message ici aussi : si le composant était déjà décoché,
        // "enabled = false" ne change rien et OnDisable n'est pas appelé.
        if (m_text != null) m_text.text = "En attente du geste";
        if (m_aimGuide != null) m_aimGuide.gameObject.SetActive(false);
    }

    // Appelé à chaque fois que le geste allume le script.
    private void OnEnable()
    {
        m_history.Clear();          // on oublie les anciens votes
        m_current = "rien";         // on peut relancer le même sort qu'avant
        m_timer = 0f;               // on analyse tout de suite
        m_failCount = 0;            // NOUVEAU : on repart de zéro à chaque geste
        m_discard = m_task != null; // une vieille analyse tourne encore ? on ignorera son résultat
        if (m_aimGuide != null) m_aimGuide.gameObject.SetActive(true);    // NOUVEAU : on montre le cercle de visée
    }

    // Appelé à chaque fois que le joueur relâche le geste.
    private void OnDisable()
    {
        if (m_text != null) m_text.text = "En attente du geste";
        if (m_aimGuide != null) m_aimGuide.gameObject.SetActive(false);   // NOUVEAU : on cache le cercle de visée
    }

    private void Update()
    {
        if (!m_cameraAccess.IsPlaying)
        {
            m_text.text = "Caméra pas prête";
            return;
        }

        UpdateAimGuide();   // NOUVEAU : à chaque image, le cercle suit l'endroit que la caméra analyse

        // --- 1. On attend la fin de l'analyse en cours ---
        if (m_task != null)
        {
            if (!m_task.IsCompleted) return;
            if (m_task.IsFaulted) Debug.LogError(m_task.Exception);
            else if (!m_discard) OnResult(m_task.Result);   // on jette les vieux résultats
            m_task = null;
            m_discard = false;
        }

        m_timer -= Time.deltaTime;
        if (m_timer > 0f) return;
        m_timer = m_interval;

        // --- 2. La zone analysée de l'image de la caméra, en gris ---
        Vector2Int res = m_cameraAccess.CurrentResolution;
        byte[] gray = ToGray(m_cameraAccess.GetColors().ToArray(), res.x, res.y, m_zone, ZoneOrigin(), out int w, out int h);

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

        // --- 5b. NOUVEAU : échecs d'affilée ---
        // N'importe quel autre résultat (un glyphe, ou rien) remet le compteur à zéro :
        // un faux cercle vu une ou deux fois dans le décor ne suffit plus.
        if (seen == "inconnu") m_failCount++;
        else m_failCount = 0;

        // Les 4 derniers messages restent affichés, pour avoir le temps de les lire
        m_messages.Enqueue(res.message);
        if (m_messages.Count > 4) m_messages.Dequeue();
        m_text.text = $"Vu : {seen} ({votes}/{m_history.Count})\nValidé : {m_current}\nÉchecs : {m_failCount}/{m_failsNeeded}\n" + string.Join("\n", m_messages);

        // --- 6. NOUVEAU : le sort rate, une seule fois, pile quand le compteur atteint le seuil ---
        if (m_failCount == m_failsNeeded) m_onFizzle.Invoke(CirclePosition(res));

        // On n'agit que quand le résultat validé change (sinon le sort se relancerait en boucle)
        if (votes < m_votesNeeded || seen == m_current) return;
        m_current = seen;
        if (seen == "rien" || seen == "inconnu") return;   // "inconnu" est géré juste au-dessus

        // --- 7. Le sort, à l'endroit du cercle ---
        m_onSpell.Invoke(seen, CirclePosition(res));
    }

    // NOUVEAU : coin en bas à gauche de la zone analysée, en fraction de l'image (de 0 à 1).
    // Zone centrée, puis décalée de m_zoneOffset, sans jamais sortir de l'image.
    private Vector2 ZoneOrigin()
    {
        float centered = (1f - m_zone) / 2f;
        return new Vector2(
            Mathf.Clamp(centered + m_zoneOffset.x, 0f, 1f - m_zone),
            Mathf.Clamp(centered + m_zoneOffset.y, 0f, 1f - m_zone));
    }

    // NOUVEAU : place le cercle de visée là où la zone analysée touche la table.
    // On lance des rayons depuis la caméra vers le centre et les bords haut/bas de la zone :
    // le centre donne la position, la distance entre les bords donne la taille.
    private void UpdateAimGuide()
    {
        if (m_aimGuide == null) return;

        Plane table = new Plane(m_table.up, m_table.position);
        Vector2 origin = ZoneOrigin();
        Vector2 center = origin + Vector2.one * (m_zone / 2f);

        if (!HitTable(table, center, out Vector3 middle)) return;   // on regarde ailleurs que la table : on ne bouge pas
        // Bords haut et bas (la zone est moins haute que large : le cercle tient ainsi en entier dedans)
        if (!HitTable(table, new Vector2(center.x, origin.y), out Vector3 bottom)) return;
        if (!HitTable(table, new Vector2(center.x, origin.y + m_zone), out Vector3 top)) return;

        m_aimGuide.position = middle + m_table.up * 0.01f;                        // 1 cm au-dessus de la table
        m_aimGuide.rotation = Quaternion.LookRotation(-m_table.up, m_table.forward); // couché, face visible vers le haut
        m_aimGuide.localScale = Vector3.one * Vector3.Distance(bottom, top);      // diamètre = hauteur de la zone sur la table
    }

    // Lance un rayon depuis la caméra vers un point de l'image (de 0 à 1) et donne l'endroit où il touche la table.
    private bool HitTable(Plane table, Vector2 viewportPoint, out Vector3 hit)
    {
        Ray ray = m_cameraAccess.ViewportPointToRay(viewportPoint);
        bool ok = table.Raycast(ray, out float dist);
        hit = ok ? ray.GetPoint(dist) : Vector3.zero;
        return ok;
    }

    // Le centre du cercle dans l'image devient un rayon qui part de la caméra.
    // Là où il touche la table, c'est le cercle.
    private Vector3 CirclePosition(GlyphResult res)
    {
        // Position dans la zone analysée (de 0 à 1), puis dans l'image complète de la caméra
        Vector2 origin = ZoneOrigin();
        Vector2 point = new Vector2(origin.x + res.cx / res.w * m_zone, origin.y + res.cy / res.h * m_zone);
        Ray ray = m_cameraAccess.ViewportPointToRay(point);
        Plane table = new Plane(m_table.up, m_table.position);
        return table.Raycast(ray, out float dist) ? ray.GetPoint(dist) : ray.GetPoint(0.5f);
    }

    // Garde une zone de l'image (zone = 1 : toute l'image), la réduit à m_maxSize et la passe en gris.
    // origin = coin en bas à gauche de la zone, en fraction de l'image.
    // Chaque pixel de sortie = moyenne des pixels qu'il recouvre (pour ne pas perdre les traits fins).
    private byte[] ToGray(Color32[] pixels, int fullW, int fullH, float zone, Vector2 origin, out int w, out int h)
    {
        int sw = (int)(fullW * zone), sh = (int)(fullH * zone);           // taille de la zone
        int offX = Mathf.Clamp((int)(origin.x * fullW), 0, fullW - sw);   // son coin en bas à gauche
        int offY = Mathf.Clamp((int)(origin.y * fullH), 0, fullH - sh);
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