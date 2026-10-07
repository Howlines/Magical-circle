using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ajoute une copie courbée de l'image de page, skinnée sur les os de la feuille qui tourne.
/// Le MeshRenderer d'origine est gardé (ImagePageLivre en dépend) mais vidé de son mesh :
/// il continue de recevoir la texture et l'état visible, et on les recopie sur la copie courbée.
/// À mettre uniquement sur les images de la feuille qui tourne (recto, verso).
/// </summary>
[DefaultExecutionOrder(1000)] // LateUpdate après ImagePageLivre, pour recopier son état à jour
public class ImagePageCourbee : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("SkinnedMeshRenderer de la feuille qui tourne (SM_Book_Page).")]
    [SerializeField] private SkinnedMeshRenderer feuilleSource;

    [Tooltip("Os de la feuille : Page_01, Page_02, Page_03. Seuls les triangles attachés à ces os servent de référence.")]
    [SerializeField] private Transform[] osFeuille;

    [Header("Maillage")]
    [Tooltip("Découpes dans la largeur de l'image (sens de la courbure).")]
    [SerializeField, Range(2, 64)] private int decoupesLargeur = 20;

    [Tooltip("Découpes dans la hauteur. Peu utile, la feuille ne se plie pas dans ce sens.")]
    [SerializeField, Range(1, 32)] private int decoupesHauteur = 4;

    [Tooltip("Inverser le sens de la grille dans la largeur (si l'image est en miroir).")]
    [SerializeField] private bool inverserLargeur = false;

    [Header("Placement")]
    [Tooltip("Distance entre l'image et la feuille, en mètres. Si la feuille traverse encore pendant un tour, augmente un peu.")]
    [SerializeField, Range(0f, 0.01f)] private float decalage = 0.002f;

    [Tooltip("Si l'image se retrouve du mauvais côté de la feuille, coche.")]
    [SerializeField] private bool inverserCote = false;

    [Header("Debug")]
    [SerializeField] private bool afficherInfos = true;

    private MeshRenderer m_RenduOrigine;
    private SkinnedMeshRenderer m_RenduCourbe;
    private MaterialPropertyBlock m_Bloc;

    private void Awake()
    {
        MeshFilter filtre = GetComponent<MeshFilter>();
        m_RenduOrigine = GetComponent<MeshRenderer>();
        if (filtre == null || filtre.sharedMesh == null || m_RenduOrigine == null)
        {
            Debug.LogWarning($"[ImagePageCourbee] {name} : pas de MeshFilter / MeshRenderer avec un mesh.", this);
            enabled = false;
            return;
        }

        if (feuilleSource == null) feuilleSource = TrouverSource();
        if (feuilleSource == null)
        {
            Debug.LogWarning($"[ImagePageCourbee] {name} : feuille source introuvable, renseigne-la à la main.", this);
            enabled = false;
            return;
        }

        Mesh grille = ConstruireGrille(filtre.sharedMesh, out int nbTriangles);
        if (grille == null)
        {
            enabled = false;
            return; // on ne touche pas au Quad d'origine si ça a échoué
        }

        // Copie courbée sur un enfant (un objet ne peut pas avoir deux renderers)
        var enfant = new GameObject("Image Courbee");
        enfant.transform.SetParent(transform, false);
        enfant.layer = gameObject.layer;

        m_RenduCourbe = enfant.AddComponent<SkinnedMeshRenderer>();
        m_RenduCourbe.sharedMesh = grille;
        m_RenduCourbe.bones = feuilleSource.bones;
        m_RenduCourbe.rootBone = feuilleSource.rootBone;
        m_RenduCourbe.sharedMaterials = m_RenduOrigine.sharedMaterials;
        m_RenduCourbe.quality = SkinQuality.Bone4;
        m_RenduCourbe.updateWhenOffscreen = true;
        m_RenduCourbe.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Le Quad d'origine n'affiche plus rien, mais reste là pour ImagePageLivre
        filtre.sharedMesh = null;

        m_Bloc = new MaterialPropertyBlock();

        if (afficherInfos)
            Debug.Log($"[ImagePageCourbee] {name} : source = {feuilleSource.name}, {nbTriangles} triangles de feuille utilisés.", this);
    }

    private void LateUpdate()
    {
        if (m_RenduCourbe == null) return;

        // Recopie ce qu'ImagePageLivre a réglé sur le renderer d'origine
        m_RenduCourbe.enabled = m_RenduOrigine.enabled;
        m_RenduOrigine.GetPropertyBlock(m_Bloc);
        m_RenduCourbe.SetPropertyBlock(m_Bloc);
    }

    private Mesh ConstruireGrille(Mesh quad, out int nbTriangles)
    {
        nbTriangles = 0;
        Mesh meshSource = feuilleSource.sharedMesh;
        Transform[] os = feuilleSource.bones;
        Matrix4x4[] bindposes = meshSource.bindposes;
        BoneWeight[] poidsSource = meshSource.boneWeights;
        Vector3[] sommetsSource = meshSource.vertices;
        int[] trianglesSource = meshSource.triangles;

        if (poidsSource.Length == 0)
        {
            Debug.LogWarning($"[ImagePageCourbee] {name} : {feuilleSource.name} n'a pas de poids d'os, ce n'est pas la bonne source.", this);
            return null;
        }

        var matricesOs = new Matrix4x4[os.Length];
        for (int i = 0; i < os.Length; i++)
            matricesOs[i] = os[i] != null ? os[i].localToWorldMatrix * bindposes[i] : Matrix4x4.identity;

        var autorises = new HashSet<int>();
        if (osFeuille != null)
            for (int i = 0; i < os.Length; i++)
                foreach (Transform o in osFeuille)
                    if (o != null && os[i] == o) autorises.Add(i);

        // Position actuelle en monde de tous les sommets de la feuille (skinning fait à la main)
        var sommetsMonde = new Vector3[sommetsSource.Length];
        for (int v = 0; v < sommetsSource.Length; v++)
            sommetsMonde[v] = MatriceSkin(poidsSource[v], matricesOs).MultiplyPoint3x4(sommetsSource[v]);

        // Triangles de la feuille (au moins un sommet attaché aux os de la feuille)
        var triangles = new List<int>();
        for (int t = 0; t < trianglesSource.Length; t += 3)
        {
            int a = trianglesSource[t], b = trianglesSource[t + 1], c = trianglesSource[t + 2];
            if (autorises.Count == 0 ||
                autorises.Contains(poidsSource[a].boneIndex0) ||
                autorises.Contains(poidsSource[b].boneIndex0) ||
                autorises.Contains(poidsSource[c].boneIndex0))
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
        }

        nbTriangles = triangles.Count / 3;
        if (nbTriangles == 0)
        {
            Debug.LogWarning($"[ImagePageCourbee] {name} : aucun triangle de feuille trouvé.", this);
            return null;
        }

        Bounds bornes = quad.bounds;
        int nx = decoupesLargeur + 1;
        int ny = decoupesHauteur + 1;
        var positions = new Vector3[nx * ny];
        var positionsLocales = new Vector3[nx * ny];
        var uvs = new Vector2[nx * ny];
        var poids = new BoneWeight[nx * ny];

        for (int y = 0; y < ny; y++)
        {
            for (int x = 0; x < nx; x++)
            {
                int i = y * nx + x;
                float u = x / (float)decoupesLargeur;
                float v = y / (float)decoupesHauteur;
                float ux = inverserLargeur ? 1f - u : u;

                Vector3 local = new Vector3(Mathf.Lerp(bornes.min.x, bornes.max.x, ux), Mathf.Lerp(bornes.min.y, bornes.max.y, v), 0f);
                positionsLocales[i] = local;
                uvs[i] = new Vector2(ux, v);

                Vector3 monde = transform.TransformPoint(local);

                // Point de la feuille le plus proche, et ses coordonnées dans le triangle
                float meilleureDist = float.MaxValue;
                int meilleurTri = 0;
                Vector3 meilleurPoint = monde, meilleurBary = Vector3.zero;
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    Vector3 p = PointLePlusProche(monde,
                        sommetsMonde[triangles[t]], sommetsMonde[triangles[t + 1]], sommetsMonde[triangles[t + 2]],
                        out Vector3 bary);
                    float d = (p - monde).sqrMagnitude;
                    if (d < meilleureDist)
                    {
                        meilleureDist = d; meilleurTri = t; meilleurPoint = p; meilleurBary = bary;
                    }
                }

                int ia = triangles[meilleurTri], ib = triangles[meilleurTri + 1], ic = triangles[meilleurTri + 2];

                // Poids interpolés comme la feuille le fait à l'intérieur de son triangle
                var cumul = new Dictionary<int, float>();
                AjouterPoids(cumul, poidsSource[ia], meilleurBary.x);
                AjouterPoids(cumul, poidsSource[ib], meilleurBary.y);
                AjouterPoids(cumul, poidsSource[ic], meilleurBary.z);
                poids[i] = GarderQuatre(cumul);

                // On garde la position le long de la feuille, mais on fixe la distance à la feuille :
                // l'image est toujours à "decalage" au-dessus, ce qui laisse une marge pendant le pli.
                Vector3 normale = Vector3.Cross(sommetsMonde[ib] - sommetsMonde[ia], sommetsMonde[ic] - sommetsMonde[ia]).normalized;
                float distance = Vector3.Dot(monde - meilleurPoint, normale);
                float cote = distance >= 0f ? 1f : -1f;
                if (inverserCote) cote = -cote;
                Vector3 recale = monde - normale * distance + normale * cote * decalage;

                positions[i] = MatriceSkin(poids[i], matricesOs).inverse.MultiplyPoint3x4(recale);
            }
        }

        var tris = new int[decoupesLargeur * decoupesHauteur * 6];
        int k = 0;
        for (int y = 0; y < decoupesHauteur; y++)
        {
            for (int x = 0; x < decoupesLargeur; x++)
            {
                int a = y * nx + x;
                int c = a + nx;
                tris[k++] = a; tris[k++] = c; tris[k++] = a + 1;
                tris[k++] = a + 1; tris[k++] = c; tris[k++] = c + 1;
            }
        }

        // Même face visible que le Quad d'origine
        Vector3 nGrille = Vector3.Cross(positionsLocales[tris[1]] - positionsLocales[tris[0]],
                                        positionsLocales[tris[2]] - positionsLocales[tris[0]]);
        if (quad.normals.Length > 0 && Vector3.Dot(nGrille, quad.normals[0]) < 0f)
        {
            for (int j = 0; j < tris.Length; j += 3)
                (tris[j + 1], tris[j + 2]) = (tris[j + 2], tris[j + 1]);
        }

        var mesh = new Mesh { name = $"{name}_Courbe" };
        mesh.vertices = positions;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.boneWeights = poids;
        mesh.bindposes = bindposes;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// Point le plus proche de p sur le triangle abc, avec ses coordonnées barycentriques
    /// (poids de a, b, c). Algorithme classique de "Real-Time Collision Detection" (Ericson).
    private static Vector3 PointLePlusProche(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) { bary = new Vector3(1, 0, 0); return a; }

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) { bary = new Vector3(0, 1, 0); return b; }

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            bary = new Vector3(1 - v, v, 0);
            return a + v * ab;
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) { bary = new Vector3(0, 0, 1); return c; }

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            bary = new Vector3(1 - w, 0, w);
            return a + w * ac;
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            bary = new Vector3(0, 1 - w, w);
            return b + w * (c - b);
        }

        float denom = 1f / (va + vb + vc);
        float v2 = vb * denom, w2 = vc * denom;
        bary = new Vector3(1 - v2 - w2, v2, w2);
        return a + ab * v2 + ac * w2;
    }

    private static Matrix4x4 MatriceSkin(BoneWeight p, Matrix4x4[] matricesOs)
    {
        Matrix4x4 m = new Matrix4x4();
        Ajouter(ref m, matricesOs[p.boneIndex0], p.weight0);
        Ajouter(ref m, matricesOs[p.boneIndex1], p.weight1);
        Ajouter(ref m, matricesOs[p.boneIndex2], p.weight2);
        Ajouter(ref m, matricesOs[p.boneIndex3], p.weight3);
        return m;
    }

    private static void Ajouter(ref Matrix4x4 m, Matrix4x4 autre, float w)
    {
        if (w <= 0f) return;
        for (int i = 0; i < 16; i++) m[i] += autre[i] * w;
    }

    private static void AjouterPoids(Dictionary<int, float> cumul, BoneWeight p, float facteur)
    {
        if (facteur <= 0f) return;
        void Un(int idx, float w)
        {
            if (w <= 0f) return;
            cumul.TryGetValue(idx, out float actuel);
            cumul[idx] = actuel + w * facteur;
        }
        Un(p.boneIndex0, p.weight0);
        Un(p.boneIndex1, p.weight1);
        Un(p.boneIndex2, p.weight2);
        Un(p.boneIndex3, p.weight3);
    }

    private static BoneWeight GarderQuatre(Dictionary<int, float> cumul)
    {
        var liste = new List<KeyValuePair<int, float>>(cumul);
        liste.Sort((x, y) => y.Value.CompareTo(x.Value));

        var idx = new int[4];
        var w = new float[4];
        float total = 0f;
        for (int i = 0; i < 4 && i < liste.Count; i++)
        {
            idx[i] = liste[i].Key;
            w[i] = liste[i].Value;
            total += w[i];
        }
        if (total <= 0f) { w[0] = 1f; total = 1f; }

        return new BoneWeight
        {
            boneIndex0 = idx[0], weight0 = w[0] / total,
            boneIndex1 = idx[1], weight1 = w[1] / total,
            boneIndex2 = idx[2], weight2 = w[2] / total,
            boneIndex3 = idx[3], weight3 = w[3] / total,
        };
    }

    private SkinnedMeshRenderer TrouverSource()
    {
        LivreVR livre = GetComponentInParent<LivreVR>();
        if (livre == null || osFeuille == null || osFeuille.Length == 0) return null;

        foreach (SkinnedMeshRenderer smr in livre.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == m_RenduCourbe) continue;
            foreach (Transform o in smr.bones)
                if (o == osFeuille[0]) return smr;
        }
        return null;
    }
}