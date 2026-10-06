using System.Collections.Generic;
using Meta.XR;
using UnityEngine;
using UnityEngine.UI;

// Test simple : détecte si un cercle FERMÉ est dessiné sur le papier.
// Principe : une forme fermée crée une "zone de fond enfermée" que l'on ne peut pas
// atteindre depuis les bords de l'image sans traverser le trait.
public class ClosedCircleTest : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private PassthroughCameraAccess m_cameraAccess;
    [SerializeField] private Text m_text;
    [SerializeField] private RawImage m_debugImage;

    [Header("Réglages")]
    [SerializeField] private float m_interval = 0.2f;                 // secondes entre deux analyses
    [SerializeField] private int m_analysisWidth = 160;               // largeur de l'image analysée (petite = rapide)
    [SerializeField, Range(0.2f, 1f)] private float m_zone = 0.6f;    // zone centrale de la caméra analysée
    [SerializeField, Range(0, 255)] private int m_threshold = 100;    // seuil noir / blanc
    [SerializeField] private bool m_inkIsLight = false;               // false = feutre sombre sur papier blanc
    [SerializeField, Range(0.001f, 0.2f)] private float m_minHoleRatio = 0.01f; // zone fermée trop petite = bruit
    [SerializeField, Range(0.1f, 1f)] private float m_maxHoleRatio = 0.6f;      // zone fermée trop grande = bord du papier
    [SerializeField] private bool m_dilate = true;                    // épaissit le trait d'1 pixel pour boucher les micro-trous
    [SerializeField] private int m_historySize = 5;                   // nombre de dernières analyses mémorisées
    [SerializeField] private int m_votesNeeded = 3;                   // combien doivent être positives pour valider

    private bool[] m_ink;        // true = pixel de trait
    private bool[] m_inkTmp;     // copie utilisée pendant la dilatation
    private int[] m_label;       // 0 = pas encore visité, -1 = fond extérieur, >0 = numéro de zone fermée
    private int[] m_stack;       // pile pour le remplissage
    private Color32[] m_debug;
    private Texture2D m_tex;
    private int m_w, m_h;
    private float m_timer;
    private readonly Queue<bool> m_history = new Queue<bool>();

    private void Update()
    {
        if (!m_cameraAccess.IsPlaying)
        {
            m_text.text = "Caméra pas prête";
            return;
        }

        m_timer -= Time.deltaTime;
        if (m_timer > 0f) return;
        m_timer = m_interval;

        float t0 = Time.realtimeSinceStartup;

        // --- 1. Récupérer l'image et découper la zone centrale ---
        Vector2Int res = m_cameraAccess.CurrentResolution;
        var pixels = m_cameraAccess.GetColors();

        int zoneW = (int)(res.x * m_zone), zoneH = (int)(res.y * m_zone);
        int offX = (res.x - zoneW) / 2, offY = (res.y - zoneH) / 2;
        int step = Mathf.Max(1, zoneW / m_analysisWidth);
        Allocate(zoneW / step, zoneH / step);

        // --- 2. Seuillage : chaque pixel devient "trait" ou "fond" ---
        for (int y = 0; y < m_h; y++)
        {
            for (int x = 0; x < m_w; x++)
            {
                Color32 p = pixels[(offY + y * step) * res.x + offX + x * step];
                int gray = (p.r * 77 + p.g * 150 + p.b * 29) >> 8;
                int i = y * m_w + x;
                m_ink[i] = m_inkIsLight ? gray > m_threshold : gray < m_threshold;
                m_label[i] = 0;
            }
        }

        // --- 2b. Dilatation : bouche les petites cassures du trait dues à la caméra ---
        if (m_dilate) Dilate();

        // --- 3. Remplir le fond depuis les bords : tout ce qu'on atteint est "dehors" ---
        for (int x = 0; x < m_w; x++) { Flood(x, 0, -1); Flood(x, m_h - 1, -1); }
        for (int y = 0; y < m_h; y++) { Flood(0, y, -1); Flood(m_w - 1, y, -1); }

        // --- 4. Le fond qui reste est enfermé par un trait : ce sont les zones fermées ---
        int total = m_w * m_h;
        int minArea = (int)(total * m_minHoleRatio);
        int maxArea = (int)(total * m_maxHoleRatio);
        var sizes = new List<int> { 0 }; // sizes[id] = taille de la zone n° id
        int holes = 0;

        for (int y = 0; y < m_h; y++)
        {
            for (int x = 0; x < m_w; x++)
            {
                int i = y * m_w + x;
                if (m_ink[i] || m_label[i] != 0) continue;

                int size = Flood(x, y, sizes.Count);
                sizes.Add(size);
                if (size >= minArea && size <= maxArea) holes++;
            }
        }

        // --- 5. Stabilité : vote sur les dernières analyses (ex : 3 positives sur les 5 dernières) ---
        m_history.Enqueue(holes > 0);
        if (m_history.Count > m_historySize) m_history.Dequeue();
        int votes = 0;
        foreach (bool b in m_history) if (b) votes++;
        bool confirmed = votes >= m_votesNeeded;

        // --- 6. Image de debug ---
        for (int i = 0; i < total; i++)
        {
            int l = m_label[i];
            if (m_ink[i]) m_debug[i] = new Color32(255, 255, 255, 255);          // trait : blanc
            else if (l == -1) m_debug[i] = new Color32(0, 0, 0, 255);            // dehors : noir
            else if (sizes[l] >= minArea && sizes[l] <= maxArea)
                m_debug[i] = new Color32(0, 200, 0, 255);                         // zone fermée valide : vert
            else m_debug[i] = new Color32(70, 70, 70, 255);                       // zone ignorée : gris
        }
        m_tex.SetPixels32(m_debug);
        m_tex.Apply();

        float ms = (Time.realtimeSinceStartup - t0) * 1000f;
        m_text.text =
            $"Cercle fermé : {(confirmed ? "OUI" : "non")}\n" +
            $"Zones fermées : {holes} (votes {votes}/{m_history.Count})\n" +
            $"Analyse {m_w}x{m_h} en {ms:F1} ms";
    }

    // Un pixel de fond devient du trait s'il touche un pixel de trait (haut, bas, gauche, droite).
    private void Dilate()
    {
        System.Array.Copy(m_ink, m_inkTmp, m_ink.Length);
        for (int y = 0; y < m_h; y++)
        {
            for (int x = 0; x < m_w; x++)
            {
                int i = y * m_w + x;
                if (m_inkTmp[i]) continue;
                if ((x > 0 && m_inkTmp[i - 1]) || (x < m_w - 1 && m_inkTmp[i + 1]) ||
                    (y > 0 && m_inkTmp[i - m_w]) || (y < m_h - 1 && m_inkTmp[i + m_w]))
                    m_ink[i] = true;
            }
        }
    }

    // Remplit une zone de fond connectée à partir de (sx, sy) avec le numéro id.
    // Renvoie le nombre de pixels de la zone.
    private int Flood(int sx, int sy, int id)
    {
        int top = 0, size = 0;
        Push(sx, sy, id, ref top);

        while (top > 0)
        {
            int i = m_stack[--top];
            size++;
            int x = i % m_w, y = i / m_w;
            Push(x - 1, y, id, ref top);
            Push(x + 1, y, id, ref top);
            Push(x, y - 1, id, ref top);
            Push(x, y + 1, id, ref top);
        }
        return size;
    }

    // Ajoute un pixel à la pile s'il est dans l'image, que c'est du fond et qu'il n'est pas encore visité.
    private void Push(int x, int y, int id, ref int top)
    {
        if (x < 0 || y < 0 || x >= m_w || y >= m_h) return;
        int i = y * m_w + x;
        if (m_ink[i] || m_label[i] != 0) return;
        m_label[i] = id;
        m_stack[top++] = i;
    }

    // Crée les tableaux et la texture de debug (une seule fois, ou si la taille change).
    private void Allocate(int w, int h)
    {
        if (m_tex != null && w == m_w && h == m_h) return;
        m_w = w; m_h = h;
        m_ink = new bool[w * h];
        m_inkTmp = new bool[w * h];
        m_label = new int[w * h];
        m_stack = new int[w * h];
        m_debug = new Color32[w * h];
        m_tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        m_debugImage.texture = m_tex;
    }
}
