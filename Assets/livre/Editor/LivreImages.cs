using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu "Tools > Livre VR > Mettre les images sur les pages".
/// Remplace les textes du livre de la scène ouverte par les images de Assets/livre/Pages/pages,
/// SANS modifier LivreVR / ZoneTournerPage : la liste "Pages" de LivreVR reçoit les noms des images,
/// les textes deviennent invisibles et une image (ImagePageLivre) s'affiche à leur place.
/// L'image de la feuille qui tourne est "skinnée" sur les os de la feuille 3D : elle se plie avec elle.
/// Relancer le menu refait les images proprement (Ctrl+Z annule).
/// </summary>
static class LivreImages
{
    const string k_DossierPages = "Assets/livre/Pages/pages";
    const string k_NomImage = "Image Page";
    const string k_SceneLivre = "LivreTest";

    // Taille de l'image sur la page, en mètres (livre à l'échelle 1). Même proportions que les PNG (1024 x 1434).
    const float k_Largeur = 0.200f;
    const float k_Hauteur = 0.280f;
    // Petit décalage au-dessus du texte d'origine, pour que l'image ne "clignote" pas avec le papier.
    const float k_Decalage = 0.0003f;
    // Découpage de l'image dans le sens de la largeur, pour qu'elle puisse se plier avec la feuille.
    const int k_Colonnes = 16;

    // Installation automatique : si le livre de la scène ouverte n'a pas encore ses images, on les met
    // (une seule fois, après la compilation ou en sortant du mode Play).
    [InitializeOnLoadMethod]
    static void InstallationAutomatique()
    {
        EditorApplication.delayCall += InstallerSiBesoin;
        EditorApplication.playModeStateChanged += etat =>
        {
            if (etat == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += InstallerSiBesoin;
        };
    }

    static void InstallerSiBesoin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        // Seulement la scène du livre : les autres scènes ne sont jamais modifiées automatiquement.
        var livre = Object.FindFirstObjectByType<LivreVR>();
        if (livre == null || livre.gameObject.scene.name != k_SceneLivre || livre.GetComponent<ImagesLivre>() != null)
            return;

        var scene = livre.gameObject.scene;
        var dejaModifiee = scene.isDirty;
        if (Appliquer(false) && !dejaModifiee)
            EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("Tools/Livre VR/Mettre les images sur les pages")]
    static void AppliquerDepuisLeMenu() => Appliquer(true);

    static bool Erreur(string message, bool demander)
    {
        if (demander)
            EditorUtility.DisplayDialog("Livre VR", message, "OK");
        else
            Debug.LogWarning("[Livre VR] " + message);
        return false;
    }

    static bool Appliquer(bool demander)
    {
        if (EditorApplication.isPlaying)
            return Erreur("Quitte d'abord le mode Play.", demander);

        var livre = Object.FindFirstObjectByType<LivreVR>();
        if (livre == null)
            return Erreur("Aucun livre (LivreVR) dans la scène ouverte.", demander);

        var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { k_DossierPages })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(chemin => System.IO.Path.GetDirectoryName(chemin).Replace('\\', '/') == k_DossierPages)
            .OrderBy(chemin => chemin, System.StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
            .ToArray();
        if (textures.Length == 0)
            return Erreur("Aucune image trouvée dans " + k_DossierPages, demander);

        var feuille = livre.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == "SM_Book_Page");
        if (feuille == null || livre.texteGauche == null || livre.texteRecto == null || livre.texteVerso == null || livre.texteSousFeuille == null)
            return Erreur("Le livre de la scène n'a pas ses textes ou sa feuille (SM_Book_Page) : rien n'a été modifié.", demander);

        var scene = livre.gameObject.scene;
        if (demander && !EditorUtility.DisplayDialog("Livre VR",
                $"Mettre les {textures.Length} images sur les pages du livre de la scène « {scene.name} » ?\n\n" +
                "La mécanique du livre (ouverture, pages) n'est pas modifiée.", "Oui", "Annuler"))
            return false;

        Undo.SetCurrentGroupName("Images du livre");
        var groupe = Undo.GetCurrentGroup();

        // 1. Liste des images + noms des pages (le contenu, pas la mécanique).
        var images = livre.GetComponent<ImagesLivre>();
        if (images == null)
            images = Undo.AddComponent<ImagesLivre>(livre.gameObject);
        Undo.RecordObject(images, "Images du livre");
        images.images = textures;
        PrefabUtility.RecordPrefabInstancePropertyModifications(images);

        Undo.RecordObject(livre, "Images du livre");
        livre.pages = textures.Select(t => t.name).ToArray();
        PrefabUtility.RecordPrefabInstancePropertyModifications(livre);

        // 2. Un matériau commun aux 4 images (enregistré dans la scène). Mat, pour ressembler à du papier.
        var materiau = new Material(Shader.Find("Standard")) { name = "Image Page Livre" };
        materiau.SetFloat("_Glossiness", 0f);

        // Échelle du livre dans la scène (le modèle est en mètres à l'échelle 1).
        var echelle = Mathf.Abs(livre.osFeuille1.parent.lossyScale.x);

        // 3. Les 4 emplacements : page de gauche et dessous de la feuille sont fixes,
        //    recto / verso de la feuille suivent ses os.
        CreerImage(livre.texteGauche, images, materiau, echelle, null);
        CreerImage(livre.texteSousFeuille, images, materiau, echelle, null);
        CreerImage(livre.texteRecto, images, materiau, echelle, feuille);
        CreerImage(livre.texteVerso, images, materiau, echelle, feuille);

        Undo.CollapseUndoOperations(groupe);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Livre VR] {textures.Length} images mises sur les pages du livre de la scène « {scene.name} ».");
        return true;
    }

    static void CreerImage(TMP_Text texte, ImagesLivre images, Material materiau, float echelle, SkinnedMeshRenderer feuille)
    {
        // Rend le texte invisible (il sert toujours à LivreVR pour dire quelle page afficher).
        Undo.RecordObject(texte, "Images du livre");
        var couleur = texte.color;
        couleur.a = 0f;
        texte.color = couleur;
        PrefabUtility.RecordPrefabInstancePropertyModifications(texte);

        var ancienne = texte.transform.Find(k_NomImage);
        if (ancienne != null)
            Undo.DestroyObjectImmediate(ancienne.gameObject);

        var go = new GameObject(k_NomImage);
        Undo.RegisterCreatedObjectUndo(go, "Images du livre");
        go.transform.SetParent(texte.transform, false);

        var mesh = CreerGrille(texte.transform, go.transform, echelle);
        mesh.name = "Image Page " + texte.name;

        if (feuille == null)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = materiau;
        }
        else
        {
            CollerSurLaFeuille(mesh, go.transform, feuille);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = feuille.bones;
            smr.rootBone = feuille.rootBone;
            smr.updateWhenOffscreen = true;
            smr.sharedMaterial = materiau;
        }

        var image = go.AddComponent<ImagePageLivre>();
        image.texteSource = texte;
        image.images = images;
    }

    /// <summary>Rectangle découpé en colonnes, posé à la place du texte, face visible du même côté que le texte.</summary>
    static Mesh CreerGrille(Transform ancre, Transform cible, float echelle)
    {
        var sommets = new Vector3[(k_Colonnes + 1) * 2];
        var uvs = new Vector2[sommets.Length];
        var normales = new Vector3[sommets.Length];
        var normale = cible.InverseTransformDirection(-ancre.forward);

        for (var i = 0; i <= k_Colonnes; i++)
        {
            var u = (float)i / k_Colonnes;
            for (var j = 0; j <= 1; j++)
            {
                var monde = ancre.position
                            + ancre.right * ((u - 0.5f) * k_Largeur * echelle)
                            + ancre.up * ((j - 0.5f) * k_Hauteur * echelle)
                            - ancre.forward * (k_Decalage * echelle);
                var k = i * 2 + j;
                sommets[k] = cible.InverseTransformPoint(monde);
                uvs[k] = new Vector2(u, j);
                normales[k] = normale;
            }
        }

        var triangles = new int[k_Colonnes * 6];
        for (var i = 0; i < k_Colonnes; i++)
        {
            int basG = i * 2, hautG = i * 2 + 1, basD = (i + 1) * 2, hautD = (i + 1) * 2 + 1;
            var t = i * 6;
            triangles[t] = basG; triangles[t + 1] = hautG; triangles[t + 2] = hautD;
            triangles[t + 3] = basG; triangles[t + 4] = hautD; triangles[t + 5] = basD;
        }

        var mesh = new Mesh { vertices = sommets, uv = uvs, normals = normales, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Donne à chaque sommet de l'image les mêmes poids d'os que le point de la feuille 3D le plus proche :
    /// l'image se déforme exactement comme la feuille quand elle tourne et se plie.
    /// </summary>
    static void CollerSurLaFeuille(Mesh mesh, Transform cible, SkinnedMeshRenderer feuille)
    {
        var cuite = new Mesh();
        feuille.BakeMesh(cuite, true);
        var versMonde = Matrix4x4.TRS(feuille.transform.position, feuille.transform.rotation, Vector3.one);
        var sommetsFeuille = cuite.vertices.Select(v => versMonde.MultiplyPoint3x4(v)).ToArray();
        var poidsFeuille = feuille.sharedMesh.boneWeights;
        Object.DestroyImmediate(cuite);

        var sommets = mesh.vertices;
        var poids = new BoneWeight[sommets.Length];
        for (var i = 0; i < sommets.Length; i++)
        {
            var monde = cible.TransformPoint(sommets[i]);
            var plusProche = 0;
            var meilleure = float.MaxValue;
            for (var k = 0; k < sommetsFeuille.Length; k++)
            {
                var d = (sommetsFeuille[k] - monde).sqrMagnitude;
                if (d < meilleure)
                {
                    meilleure = d;
                    plusProche = k;
                }
            }
            poids[i] = poidsFeuille[plusProche];
        }

        var os = feuille.bones;
        var bindposes = new Matrix4x4[os.Length];
        for (var b = 0; b < os.Length; b++)
            bindposes[b] = os[b].worldToLocalMatrix * cible.localToWorldMatrix;

        mesh.boneWeights = poids;
        mesh.bindposes = bindposes;
    }
}
