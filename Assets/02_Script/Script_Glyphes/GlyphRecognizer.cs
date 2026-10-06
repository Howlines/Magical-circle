using System;
using System.Collections.Generic;
using UnityEngine;

// Reconnaît un glyphe dessiné dans un cercle.
// Entrée : une image en gris (byte[] de w*h, 0 = noir, 255 = blanc).
// Le pixel (x, y) est à l'index y * w + x.
// Aucune fonction de Unity n'est appelée, donc on peut l'utiliser sur un autre thread.
namespace MagicCircle
{
    public class GlyphResult
    {
        public bool circleFound;     // un cercle a été vu
        public string glyph;         // le glyphe reconnu, ou null
        public float score;          // score du meilleur glyphe (0 à 1)
        public float cx, cy;         // centre du cercle en pixels
        public int w, h;             // taille de l'image analysée
        public string message = "";  // explication du résultat
    }

    [Serializable]
    public class GlyphRecognizer
    {
        [Header("Encre")]
        [SerializeField] private int m_thresholdRadius = 20;    // taille du voisinage comparé (bien plus grand que le trait)
        [SerializeField] private int m_thresholdC = 20;         // de combien un pixel doit être plus sombre que ses voisins
        [SerializeField] private int m_minSpeckPixels = 15;     // forme plus petite = poussière

        [Header("Cercle")]
        [SerializeField] private int m_minRingPixels = 300;     // forme plus petite = pas le cercle
        [SerializeField] private int m_ringDilate = 1;          // épaissit le trait de N px pour chercher le cercle (recolle les cassures)
        [SerializeField] private float m_ringTolerance = 0.2f;  // un pixel colle à l'ellipse s'il est à moins de 20 % du rayon
        [SerializeField] private float m_minRingFit = 0.4f;     // part mini des pixels qui collent à l'ellipse
        [SerializeField] private float m_maxGapDegrees = 40f;   // plus grand trou autorisé dans le cercle
        [SerializeField] private float m_minRoundness = 0.2f;   // petit rayon / grand rayon mini (1 = vu de face, 0.5 = vu à 60°)
        [SerializeField] private int m_interiorMargin = 2;      // on rogne l'intérieur pour ne pas garder de bouts du cercle

        [Header("Comparaison")]
        [SerializeField] private float m_distanceScale = 3f;    // score = exp(-distance / distanceScale)
        [SerializeField] private int m_rotations = 36;          // angles testés (36 = tous les 10°)
        [SerializeField] private float m_acceptScore = 0.45f;   // score mini pour accepter un glyphe
        [SerializeField] private float m_minMargin = 0.06f;     // avance mini du 1er sur le 2e

        private const int Work = 128;   // taille de l'image redressée
        private const int N = 64;       // taille de la grille de comparaison

        // Les références : "earth", "earth_b"... (avant le _ = le nom du glyphe)
        private readonly Dictionary<string, Shape> m_references = new Dictionary<string, Shape>();

        public bool AddReference(string name, byte[] gray, int w, int h, out string error)
        {
            GlyphResult res = Extract(gray, w, h, out bool[] strokes);
            error = res.message;
            if (strokes == null) return false;
            m_references[name] = new Shape(strokes);
            return true;
        }

        public GlyphResult Analyse(byte[] gray, int w, int h)
        {
            GlyphResult res = Extract(gray, w, h, out bool[] strokes);
            if (strokes != null) Compare(res, strokes);
            return res;
        }

        // =====================================================================
        //  TROUVER LE GLYPHE DANS L'IMAGE
        //  Renvoie le trait du glyphe redressé en 64x64 (strokes), ou null si ça rate.
        // =====================================================================
        private GlyphResult Extract(byte[] gray, int w, int h, out bool[] strokes)
        {
            GlyphResult res = new GlyphResult { w = w, h = h };
            strokes = null;

            // --- 1. Trouver l'encre ---
            bool[] ink = FindInk(gray, w, h);

            // --- 2. Regrouper l'encre en formes, et effacer les poussières ---
            int[] labels = FindShapes(ink, w, h, out List<int> sizes);
            for (int i = 0; i < w * h; i++)
            {
                if (labels[i] >= 0 && sizes[labels[i]] < m_minSpeckPixels)
                {
                    ink[i] = false;
                    labels[i] = -1;
                }
            }

            // --- 3. Trouver le cercle : la plus grande des 6 plus grosses formes qui en est un ---
            // On cherche le cercle sur une copie épaissie de l'encre : vu de biais, le côté loin
            // devient très fin et se casse par endroits, l'épaissir recolle les morceaux.
            if (m_ringDilate > 0)
            {
                bool[] thick = ink;
                for (int k = 0; k < m_ringDilate; k++) thick = Dilate(thick, w, h);
                labels = FindShapes(thick, w, h, out sizes);
            }
            List<int> candidates = new List<int>();
            for (int i = 0; i < sizes.Count; i++)
            {
                if (sizes[i] >= m_minRingPixels) candidates.Add(i);
            }
            candidates.Sort((x, y) => sizes[y].CompareTo(sizes[x]));
            if (candidates.Count > 6) candidates.RemoveRange(6, candidates.Count - 6);

            Ellipse ring = null;
            res.message = "Aucun cercle trouvé";
            foreach (int lab in candidates)
            {
                Ellipse e = TestRing(labels, lab, w, h, out string reason);
                if (e == null)
                {
                    if (ring == null) res.message = reason;
                    continue;
                }
                if (ring == null || e.a * e.b > ring.a * ring.b) ring = e;
            }
            if (ring == null) return res;

            res.circleFound = true;
            res.cx = ring.cx;
            res.cy = ring.cy;

            // --- 4. L'intérieur du cercle = l'ellipse un peu rétrécie ---
            bool[] interior = new bool[w * h];
            float limit = (1 - m_ringTolerance) * (1 - m_ringTolerance);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (labels[i] == ring.label) continue;
                    ring.ToCircle(x, y, out float u, out float v);
                    interior[i] = u * u + v * v < limit;
                }
            }
            for (int k = 0; k < m_interiorMargin; k++) interior = Erode(interior, w, h);

            // --- 5. Le glyphe = l'encre à l'intérieur ---
            float[] glyph = new float[w * h];
            int glyphPixels = 0;
            for (int i = 0; i < w * h; i++)
            {
                if (ink[i] && interior[i])
                {
                    glyph[i] = 1f;
                    glyphPixels++;
                }
            }
            if (glyphPixels < 20)
            {
                res.message = "Cercle vide : pas de glyphe à l'intérieur";
                return res;
            }

            // --- 6. Le redresser ---
            strokes = Straighten(glyph, w, h, ring);
            res.message = "";
            return res;
        }

        // =====================================================================
        //  1. ENCRE
        //  Un pixel est de l'encre s'il est plus sombre que la moyenne de ses voisins
        //  (comme ça les ombres ne gênent pas).
        //  Pour avoir vite la somme d'un carré, on fait une "image intégrale" :
        //  chaque case contient la somme de tout ce qui est au-dessus et à gauche,
        //  et la somme d'un carré = D - B - C + A (ses 4 coins).
        // =====================================================================
        private bool[] FindInk(byte[] g, int w, int h)
        {
            int r = m_thresholdRadius;
            long c = m_thresholdC;

            long[] integral = new long[(w + 1) * (h + 1)];   // une ligne et une colonne de 0 en plus
            for (int y = 0; y < h; y++)
            {
                long somme = 0;
                for (int x = 0; x < w; x++)
                {
                    somme += g[y * w + x];
                    integral[(y + 1) * (w + 1) + x + 1] = integral[y * (w + 1) + x + 1] + somme;
                }
            }

            bool[] ink = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                int y0 = Math.Max(0, y - r), y1 = Math.Min(h - 1, y + r);
                for (int x = 0; x < w; x++)
                {
                    int x0 = Math.Max(0, x - r), x1 = Math.Min(w - 1, x + r);
                    long sum = integral[(y1 + 1) * (w + 1) + x1 + 1]   // D
                             - integral[y0 * (w + 1) + x1 + 1]         // B
                             - integral[(y1 + 1) * (w + 1) + x0]       // C
                             + integral[y0 * (w + 1) + x0];            // A
                    int nb = (x1 - x0 + 1) * (y1 - y0 + 1);

                    // pixel < moyenne - c  (multiplié par nb pour éviter la division)
                    ink[y * w + x] = g[y * w + x] * nb < sum - c * nb;
                }
            }
            return ink;
        }

        // =====================================================================
        //  2. FORMES
        //  Comme le pot de peinture : chaque bout d'encre relié reçoit un numéro.
        //  labels[i] = numéro de la forme (-1 = pas d'encre), sizes[n] = nombre de pixels.
        // =====================================================================
        private static int[] FindShapes(bool[] ink, int w, int h, out List<int> sizes)
        {
            int[] labels = new int[w * h];
            for (int i = 0; i < labels.Length; i++) labels[i] = -1;
            sizes = new List<int>();
            Stack<int> stack = new Stack<int>();

            for (int start = 0; start < w * h; start++)
            {
                if (!ink[start] || labels[start] >= 0) continue;

                int lab = sizes.Count;
                int size = 0;
                labels[start] = lab;
                stack.Push(start);

                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    size++;
                    int px = p % w, py = p / w;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = px + dx, ny = py + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int q = ny * w + nx;
                            if (ink[q] && labels[q] < 0)
                            {
                                labels[q] = lab;
                                stack.Push(q);
                            }
                        }
                    }
                }
                sizes.Add(size);
            }
            return labels;
        }

        // =====================================================================
        //  3. CETTE FORME EST-ELLE LE CERCLE ?
        //  - on mesure son ellipse, puis on la remesure plusieurs fois en ignorant
        //    ce qui n'y colle pas (un bout de bord de feuille par exemple)
        //  - assez de pixels doivent coller à l'ellipse (un triangle, non)
        //  - le tour doit être presque complet (un petit trou est accepté)
        //  Renvoie l'ellipse, ou null.
        // =====================================================================
        private Ellipse TestRing(int[] labels, int lab, int w, int h, out string reason)
        {
            reason = "Pas de cercle : la plus grande forme ne ressemble pas à un cercle";
            bool[] mask = new bool[w * h];
            for (int i = 0; i < w * h; i++) mask[i] = labels[i] == lab;

            Ellipse e = Ellipse.Fit(mask, w, h);
            if (e == null) return null;
            e.label = lab;

            float[] tolerances = { 0.5f, 0.4f, 0.3f, 0.25f, 0.2f, 0.2f };
            bool[] close = new bool[w * h];
            foreach (float tol in tolerances)
            {
                int count = 0;
                for (int i = 0; i < w * h; i++)
                {
                    close[i] = mask[i] && Math.Abs(e.Rho(i % w, i / w) - 1) < tol;
                    if (close[i]) count++;
                }
                if (count < 50) break;
                Ellipse better = Ellipse.Fit(close, w, h);
                if (better == null) break;
                better.label = lab;
                e = better;
            }

            if (e.b / e.a < m_minRoundness)
            {
                reason = $"Forme trop aplatie pour être le cercle ({e.b / e.a:0.00})";
                return null;
            }

            // On découpe le tour en 72 parts de 5° et on note celles où passe le trait
            const int Parts = 72;
            bool[] filled = new bool[Parts];
            int total = 0, near = 0;
            for (int i = 0; i < w * h; i++)
            {
                if (!mask[i]) continue;
                total++;
                e.ToCircle(i % w, i / w, out float u, out float v);
                float rho = (float)Math.Sqrt(u * u + v * v);
                if (Math.Abs(rho - 1) < m_ringTolerance)
                {
                    near++;
                    int part = (int)((Math.Atan2(v, u) + Math.PI) / (2 * Math.PI) * Parts) % Parts;
                    filled[part] = true;
                }
            }
            float fit = (float)near / total;

            // Le trou = la plus longue suite de parts vides (2 tours, car la dernière touche la première)
            int longest = 0, run = 0;
            for (int k = 0; k < 2 * Parts; k++)
            {
                run = filled[k % Parts] ? 0 : run + 1;
                longest = Math.Max(longest, run);
            }
            float gap = Math.Min(360f, longest * 360f / Parts);

            if (fit < m_minRingFit)
            {
                reason = $"Pas de cercle : la plus grande forme ne ressemble pas à un cercle ({fit:0.00})";
                return null;
            }
            if (gap > m_maxGapDegrees)
            {
                reason = $"Cercle pas fermé : il manque {gap:0}°";
                return null;
            }
            return e;
        }

        // =====================================================================
        //  6. REDRESSER LE GLYPHE
        //  On fabrique une image de 128x128 où le cercle est remis rond, centrée
        //  sur le glyphe et agrandie pour le remplir. Pour chaque pixel de cette
        //  image, on calcule d'où il vient dans la photo. Puis on réduit en 64x64.
        // =====================================================================
        private static bool[] Straighten(float[] src, int w, int h, Ellipse e)
        {
            float c = (float)Math.Cos(e.angle), s = (float)Math.Sin(e.angle);

            // --- Centre et taille du glyphe une fois le cercle remis rond ---
            List<float> pts = new List<float>();
            double sx = 0, sy = 0;
            int n = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (src[y * w + x] <= 0) continue;
                    float dx = x + 0.5f - e.cx, dy = y + 0.5f - e.cy;
                    float qx = c * dx + s * dy, qy = -s * dx + c * dy;   // grand axe mis à plat
                    qy *= e.a / e.b;                                     // petit axe étiré
                    float rx = c * qx - s * qy, ry = s * qx + c * qy;    // retour dans le bon sens
                    pts.Add(rx); pts.Add(ry);
                    sx += rx; sy += ry;
                    n++;
                }
            }
            float mx = (float)(sx / n), my = (float)(sy / n);

            // Rayon = distance qui contient 98 % des pixels (une poussière isolée ne compte pas)
            float[] dist = new float[n];
            for (int i = 0; i < n; i++)
            {
                float ddx = pts[2 * i] - mx, ddy = pts[2 * i + 1] - my;
                dist[i] = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
            }
            Array.Sort(dist);
            float radius = Math.Max(5f, dist[Math.Min(n - 1, (int)(n * 0.98f))]);
            float k = (Work / 2f - 6f) / radius;   // le rayon touche presque le bord

            // --- Image redressée 128x128, puis réduction en 64x64 ---
            bool[] strokes = new bool[N * N];
            for (int v = 0; v < Work; v++)
            {
                for (int u = 0; u < Work; u++)
                {
                    float px = (u + 0.5f - Work / 2f) / k + mx, py = (v + 0.5f - Work / 2f) / k + my;
                    float qx = c * px + s * py, qy = -s * px + c * py;   // grand axe mis à plat
                    qy *= e.b / e.a;                                     // écrasé comme sur la photo
                    float rx = c * qx - s * qy, ry = s * qx + c * qy;    // retour dans le sens de la photo

                    // Une case 64x64 est du trait si un de ses 4 pixels l'est
                    if (Bilinear(src, w, h, e.cx + rx, e.cy + ry) > 0.3f)
                        strokes[(v * N / Work) * N + u * N / Work] = true;
                }
            }
            return strokes;
        }

        // =====================================================================
        //  7. COMPARAISON
        //  Pour chaque référence, on cherche la rotation (et une taille à ±10 %)
        //  où les traits sont le plus proches. Meilleur score par glyphe
        //  (earth et earth_b comptent pour earth). Le sort marche si le 1er
        //  dépasse acceptScore ET devance assez le 2e.
        // =====================================================================
        private void Compare(GlyphResult res, bool[] strokes)
        {
            if (m_references.Count == 0)
            {
                res.message = "Aucune référence chargée";
                return;
            }

            Shape drawing = new Shape(strokes);
            float[] sizes = { 0.9f, 1f, 1.1f };
            Dictionary<string, float> perGlyph = new Dictionary<string, float>();
            string best = null;
            float bestScore = 0, bestRotation = 0;

            foreach (KeyValuePair<string, Shape> reference in m_references)
            {
                float score = 0, rotation = 0;
                for (int k = 0; k < m_rotations; k++)
                {
                    double rad = 2 * Math.PI * k / m_rotations;
                    float d = float.MaxValue;
                    foreach (float size in sizes) d = Math.Min(d, Shape.Distance(drawing, reference.Value, rad, size));
                    float s = (float)Math.Exp(-d / m_distanceScale);
                    if (s > score)
                    {
                        score = s;
                        rotation = 360f * k / m_rotations;
                    }
                }

                string name = reference.Key.Split('_')[0].ToLowerInvariant();   // "earth_b" -> "earth"
                if (!perGlyph.TryGetValue(name, out float prev) || score > prev) perGlyph[name] = score;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRotation = rotation;
                    best = name;
                }
            }

            // Le 2e = le meilleur des AUTRES glyphes
            float second = 0;
            foreach (KeyValuePair<string, float> kv in perGlyph)
            {
                if (kv.Key != best && kv.Value > second) second = kv.Value;
            }

            res.score = bestScore;
            if (bestScore < m_acceptScore || bestScore - second < m_minMargin)
            {
                res.message = $"Pas sûr : {best} à {bestScore:0.00} (2e : {second:0.00}) → le sort rate";
            }
            else
            {
                res.glyph = best;
                res.message = $"Reconnu : {best} ({bestScore:0.00}, rotation {bestRotation}°)";
            }
        }

        // =====================================================================
        //  OUTILS
        // =====================================================================

        // Les formes grossissent d'un pixel : un pixel s'allume si un de ses 8 voisins est allumé
        private static bool[] Dilate(bool[] m, int w, int h)
        {
            bool[] o = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool on = false;
                    for (int dy = -1; dy <= 1 && !on; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !on; dx++)
                        {
                            int nx = Math.Min(w - 1, Math.Max(0, x + dx));
                            int ny = Math.Min(h - 1, Math.Max(0, y + dy));
                            on = m[ny * w + nx];
                        }
                    }
                    o[y * w + x] = on;
                }
            }
            return o;
        }

        // Les formes maigrissent d'un pixel : un pixel s'éteint si un de ses 8 voisins est éteint
        private static bool[] Erode(bool[] m, int w, int h)
        {
            bool[] o = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool on = true;
                    for (int dy = -1; dy <= 1 && on; dy++)
                    {
                        for (int dx = -1; dx <= 1 && on; dx++)
                        {
                            int nx = Math.Min(w - 1, Math.Max(0, x + dx));   // aux bords, on prend le pixel le plus proche
                            int ny = Math.Min(h - 1, Math.Max(0, y + dy));
                            on = m[ny * w + nx];
                        }
                    }
                    o[y * w + x] = on;
                }
            }
            return o;
        }

        // Lit une image entre deux pixels en mélangeant les 4 pixels autour
        private static float Bilinear(float[] img, int w, int h, float x, float y)
        {
            x -= 0.5f; y -= 0.5f;   // le centre du pixel (10, 5) est en (10.5, 5.5)
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            return (Px(img, w, h, x0, y0)     * (1 - fx) + Px(img, w, h, x0 + 1, y0)     * fx) * (1 - fy)
                 + (Px(img, w, h, x0, y0 + 1) * (1 - fx) + Px(img, w, h, x0 + 1, y0 + 1) * fx) * fy;
        }

        // Un pixel, ou 0 en dehors de l'image
        private static float Px(float[] img, int w, int h, int x, int y)
        {
            return (x < 0 || y < 0 || x >= w || y >= h) ? 0f : img[y * w + x];
        }

        // =====================================================================
        //  ELLIPSE : le cercle vu de biais
        // =====================================================================
        private class Ellipse
        {
            public float cx, cy;   // centre
            public float a, b;     // grand et petit rayon
            public float angle;    // inclinaison du grand axe (radians)
            public int label;      // numéro de la forme
            private float m_cos, m_sin;

            // Position d'un pixel dans le repère de l'ellipse remise en cercle de rayon 1
            public void ToCircle(int x, int y, out float u, out float v)
            {
                float dx = x - cx, dy = y - cy;
                u = (m_cos * dx + m_sin * dy) / a;
                v = (-m_sin * dx + m_cos * dy) / b;
            }

            // 1 = pile sur l'ellipse, < 1 dedans, > 1 dehors
            public float Rho(int x, int y)
            {
                ToCircle(x, y, out float u, out float v);
                return (float)Math.Sqrt(u * u + v * v);
            }

            // Mesure l'ellipse qui colle le mieux aux pixels (null si ce n'en est pas une).
            // Une ellipse a une équation  A x² + B xy + C y² + D x + E y = 1 :
            // on cherche les 5 nombres qui collent le mieux à tous les pixels
            // (moindres carrés), puis on en déduit centre, rayons et angle.
            public static Ellipse Fit(bool[] mask, int w, int h)
            {
                // On prend au plus ~6000 points (un sur "step") pour aller vite
                int n = 0;
                for (int i = 0; i < w * h; i++) if (mask[i]) n++;
                if (n < 20) return null;
                int step = Math.Max(1, n / 6000);

                // On recentre les points et on les ramène autour de 1 (sinon les x² écrasent tout)
                double mx = 0, my = 0;
                int m = 0, k = 0;
                for (int i = 0; i < w * h; i++)
                {
                    if (mask[i] && (k++ % step) == 0) { mx += i % w; my += i / w; m++; }
                }
                mx /= m; my /= m;

                double scale = 0;
                k = 0;
                for (int i = 0; i < w * h; i++)
                {
                    if (mask[i] && (k++ % step) == 0)
                    {
                        double dx = i % w - mx, dy = i / w - my;
                        scale += dx * dx + dy * dy;
                    }
                }
                scale = Math.Sqrt(scale / m);
                if (scale < 1e-6) return null;

                // Le système à résoudre : chaque point donne la ligne [x², xy, y², x, y]
                double[,] M = new double[5, 5];
                double[] V = new double[5];
                double[] row = new double[5];
                k = 0;
                for (int i = 0; i < w * h; i++)
                {
                    if (!mask[i] || (k++ % step) != 0) continue;
                    double x = (i % w - mx) / scale, y = (i / w - my) / scale;
                    row[0] = x * x; row[1] = x * y; row[2] = y * y; row[3] = x; row[4] = y;
                    for (int r = 0; r < 5; r++)
                    {
                        V[r] += row[r];
                        for (int c = 0; c < 5; c++) M[r, c] += row[r] * row[c];
                    }
                }
                double[] coef = Solve(M, V);
                if (coef == null) return null;
                double A = coef[0], B = coef[1], C = coef[2], D = coef[3], E = coef[4];

                // C'est une ellipse seulement si B² - 4AC < 0
                if (B * B - 4 * A * C >= 0) return null;

                // Le centre
                double x0 = (B * E - 2 * C * D) / (4 * A * C - B * B);
                double y0 = (B * D - 2 * A * E) / (4 * A * C - B * B);
                double F = 1 - (A * x0 * x0 + B * x0 * y0 + C * y0 * y0 + D * x0 + E * y0);
                if (F <= 0) return null;

                // Les rayons et l'angle
                double tr = A + C, dt = Math.Sqrt((A - C) * (A - C) + B * B);
                double l1 = (tr - dt) / 2, l2 = (tr + dt) / 2;
                if (l1 <= 0 || l2 <= 0) return null;
                double th = Math.Abs(B) < 1e-12 ? (A <= C ? 0 : Math.PI / 2) : Math.Atan2(l1 - A, B / 2);

                Ellipse e = new Ellipse
                {
                    cx = (float)(x0 * scale + mx),
                    cy = (float)(y0 * scale + my),
                    a = (float)(Math.Sqrt(F / l1) * scale),
                    b = (float)(Math.Sqrt(F / l2) * scale),
                    angle = (float)th
                };
                e.m_cos = (float)Math.Cos(e.angle);   // calculés une fois pour toutes
                e.m_sin = (float)Math.Sin(e.angle);
                if (float.IsNaN(e.a) || float.IsNaN(e.b) || float.IsNaN(e.cx) || float.IsNaN(e.cy)) return null;
                return e;
            }

            // Résout M x = V (pivot de Gauss)
            private static double[] Solve(double[,] M, double[] V)
            {
                int n = V.Length;
                double[,] A = (double[,])M.Clone();
                double[] b = (double[])V.Clone();

                for (int col = 0; col < n; col++)
                {
                    // On remonte la ligne avec le plus grand nombre dans cette colonne
                    int piv = col;
                    for (int r = col + 1; r < n; r++) if (Math.Abs(A[r, col]) > Math.Abs(A[piv, col])) piv = r;
                    if (Math.Abs(A[piv, col]) < 1e-12) return null;
                    for (int c = 0; c < n; c++) { double t = A[col, c]; A[col, c] = A[piv, c]; A[piv, c] = t; }
                    { double t = b[col]; b[col] = b[piv]; b[piv] = t; }

                    // On annule cette colonne dans les lignes du dessous
                    for (int r = col + 1; r < n; r++)
                    {
                        double f = A[r, col] / A[col, col];
                        for (int c = col; c < n; c++) A[r, c] -= f * A[col, c];
                        b[r] -= f * b[col];
                    }
                }

                // On remonte pour trouver les inconnues une par une
                double[] x = new double[n];
                for (int r = n - 1; r >= 0; r--)
                {
                    double s = b[r];
                    for (int c = r + 1; c < n; c++) s -= A[r, c] * x[c];
                    x[r] = s / A[r, r];
                }
                return x;
            }
        }

        // =====================================================================
        //  SHAPE : la "signature" d'un glyphe pour la comparaison
        //  - les points du trait (grille 64x64, centrés sur 0)
        //  - la carte des distances : pour chaque case, la distance au trait le plus proche
        //
        //  Distance dessin -> référence : pour chaque point du dessin, on lit dans la
        //  carte de la référence à quelle distance est son trait, et on fait la moyenne.
        //  Pareil dans l'autre sens (pour les morceaux manquants), et on garde le pire.
        // =====================================================================
        private class Shape
        {
            private const float Outside = 20f;   // distance comptée pour un point qui sort de la grille
            private readonly float[] m_px, m_py;
            private readonly float[] m_dist;

            public Shape(bool[] strokes)
            {
                List<float> xs = new List<float>(), ys = new List<float>();
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        if (!strokes[y * N + x]) continue;
                        xs.Add(x + 0.5f - N / 2f);
                        ys.Add(y + 0.5f - N / 2f);
                    }
                }
                m_px = xs.ToArray();
                m_py = ys.ToArray();
                m_dist = DistanceMap(strokes);
            }

            // Le pire des deux sens : sinon un glyphe plein de traits gagne trop souvent
            public static float Distance(Shape drawing, Shape reference, double rad, float size)
            {
                float d1 = OneWay(drawing, reference, rad, size);
                float d2 = OneWay(reference, drawing, -rad, 1f / size);
                return Math.Max(d1, d2);
            }

            // Distance moyenne des points de "from" (tournés et agrandis) jusqu'au trait de "to"
            private static float OneWay(Shape from, Shape to, double rad, float size)
            {
                if (from.m_px.Length == 0) return Outside;
                float c = (float)Math.Cos(rad) * size, s = (float)Math.Sin(rad) * size, somme = 0;
                for (int i = 0; i < from.m_px.Length; i++)
                {
                    float x = c * from.m_px[i] - s * from.m_py[i] + N / 2f;
                    float y = s * from.m_px[i] + c * from.m_py[i] + N / 2f;
                    int ix = (int)x, iy = (int)y;
                    somme += (ix < 0 || iy < 0 || ix >= N || iy >= N) ? Outside : to.m_dist[iy * N + ix];
                }
                return somme / from.m_px.Length;
            }

            // Carte des distances en 2 passages (aller puis retour).
            // Un pas droit coûte 3, un pas en diagonale 4 (environ 3 x racine de 2).
            private static float[] DistanceMap(bool[] s)
            {
                int[] d = new int[N * N];
                for (int i = 0; i < N * N; i++) d[i] = s[i] ? 0 : 1 << 20;

                // Aller : on regarde en haut et à gauche
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        int i = y * N + x;
                        if (x > 0) d[i] = Math.Min(d[i], d[i - 1] + 3);
                        if (y > 0) d[i] = Math.Min(d[i], d[i - N] + 3);
                        if (y > 0 && x > 0) d[i] = Math.Min(d[i], d[i - N - 1] + 4);
                        if (y > 0 && x < N - 1) d[i] = Math.Min(d[i], d[i - N + 1] + 4);
                    }
                }

                // Retour : on regarde en bas et à droite
                for (int y = N - 1; y >= 0; y--)
                {
                    for (int x = N - 1; x >= 0; x--)
                    {
                        int i = y * N + x;
                        if (x < N - 1) d[i] = Math.Min(d[i], d[i + 1] + 3);
                        if (y < N - 1) d[i] = Math.Min(d[i], d[i + N] + 3);
                        if (y < N - 1 && x < N - 1) d[i] = Math.Min(d[i], d[i + N + 1] + 4);
                        if (y < N - 1 && x > 0) d[i] = Math.Min(d[i], d[i + N - 1] + 4);
                    }
                }

                float[] f = new float[N * N];
                for (int i = 0; i < N * N; i++) f[i] = d[i] / 3f;   // en cases
                return f;
            }
        }
    }
}