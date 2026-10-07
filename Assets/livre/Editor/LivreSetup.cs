using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Menu "Tools > Livre VR > Placer le livre dans la scène".
/// Construit le livre articulé : attrapable, s'ouvre en touchant la couverture, pages tournées au doigt,
/// textes collés aux pages. Relancer le menu remplace le livre existant.
///
/// Repères du modèle (mesurés sur SM_FantasyBook_MiddleOpen, en mètres) :
/// reliure le long de Z en x = 0, livre fermé posé sur y = 0 vers -X, ouvert il s'étend de x = -0.225 à +0.225.
/// Le lecteur se tient côté +Z : la page de droite est en -X, celle de gauche en +X.
/// </summary>
static class LivreSetup
{
    const string k_CheminModele = "Assets/livre/SM_FantasyBook_MiddleOpen.fbx";
    const string k_NomLivre = "Livre VR";

    const float k_HauteurPage = 0.032f;     // Hauteur des pages quand le livre est ouvert
    const float k_CentrePageX = 0.112f;     // Distance entre la reliure et le centre d'une page
    static readonly Vector2 k_TailleTexte = new Vector2(0.15f, 0.24f);
    static readonly Color k_CouleurEncre = new Color(0.18f, 0.11f, 0.06f);

    [MenuItem("Tools/Livre VR/Placer le livre dans la scène")]
    static void PlacerLivre()
    {
        var modele = AssetDatabase.LoadAssetAtPath<GameObject>(k_CheminModele);
        if (modele == null)
        {
            EditorUtility.DisplayDialog("Livre VR", "Modèle introuvable : " + k_CheminModele, "OK");
            return;
        }

        var ancien = GameObject.Find(k_NomLivre);
        if (ancien != null)
            Undo.DestroyObjectImmediate(ancien);

        var position = TrouverPosition(out var surUneSurface);

        // --- Racine + modèle -----------------------------------------------------------------
        var racine = new GameObject(k_NomLivre);
        Undo.RegisterCreatedObjectUndo(racine, "Placer le livre");

        var visuel = (GameObject)PrefabUtility.InstantiatePrefab(modele, racine.transform);
        visuel.name = "Modele";
        visuel.transform.localPosition = Vector3.zero;
        visuel.transform.localRotation = Quaternion.identity;

        // Les os sont pilotés par LivreVR : pas d'Animator qui pourrait les écraser.
        var animator = visuel.GetComponent<Animator>();
        if (animator != null)
            Object.DestroyImmediate(animator);

        // Les bounds des meshes skinnés changent quand le livre s'ouvre : évite qu'il disparaisse à l'écran.
        foreach (var smr in visuel.GetComponentsInChildren<SkinnedMeshRenderer>())
            smr.updateWhenOffscreen = true;

        var osOuverture = Trouver(visuel.transform, "Book_Open");
        var osPagesGauche = Trouver(visuel.transform, "Pages_Left");
        var osFeuille1 = Trouver(visuel.transform, "Page_01");
        var osFeuille2 = Trouver(visuel.transform, "Page_02");
        var osFeuille3 = Trouver(visuel.transform, "Page_03");
        if (osOuverture == null || osPagesGauche == null || osFeuille1 == null || osFeuille2 == null || osFeuille3 == null)
        {
            Undo.DestroyObjectImmediate(racine);
            EditorUtility.DisplayDialog("Livre VR", "Les os du livre (Book_Open, Pages_Left, Page_01...) sont introuvables.", "OK");
            return;
        }

        var livre = racine.AddComponent<LivreVR>();
        livre.osOuverture = osOuverture;
        livre.osFeuille1 = osFeuille1;
        livre.osFeuille2 = osFeuille2;
        livre.osFeuille3 = osFeuille3;

        // --- Textes : placés en pose "ouvert", puis collés aux os pour suivre les pages ----------
        var repOuverture = osOuverture.localRotation;
        var repFeuille = osFeuille1.localRotation;
        osOuverture.localRotation = Quaternion.AngleAxis(180f, Vector3.up) * repOuverture;

        livre.texteGauche = CreerTexte("Texte Page Gauche", new Vector3(k_CentrePageX, k_HauteurPage + 0.0003f, 0f), osPagesGauche, "Page 1");
        livre.texteRecto = CreerTexte("Texte Feuille Recto", new Vector3(-k_CentrePageX, k_HauteurPage + 0.0006f, 0f), osFeuille1, "Page 2");
        livre.texteSousFeuille = CreerTexte("Texte Sous La Feuille", new Vector3(-k_CentrePageX, k_HauteurPage - 0.0004f, 0f), racine.transform, "Page 4");

        osFeuille1.localRotation = Quaternion.AngleAxis(180f, Vector3.up) * repFeuille;
        livre.texteVerso = CreerTexte("Texte Feuille Verso", new Vector3(k_CentrePageX, k_HauteurPage + 0.0008f, 0f), osFeuille1, "Page 3");

        osFeuille1.localRotation = repFeuille;
        osOuverture.localRotation = repOuverture;

        // --- Physique + attraper -------------------------------------------------------------
        livre.colliderFermeCentre = new Vector3(-0.104f, 0.037f, 0f);
        livre.colliderFermeTaille = new Vector3(0.242f, 0.075f, 0.31f);
        livre.colliderOuvertCentre = new Vector3(0f, 0.016f, 0f);
        livre.colliderOuvertTaille = new Vector3(0.45f, 0.04f, 0.31f);

        var collider = racine.AddComponent<BoxCollider>();
        collider.center = livre.colliderFermeCentre;
        collider.size = livre.colliderFermeTaille;
        livre.colliderLivre = collider;

        var rb = racine.AddComponent<Rigidbody>();
        rb.mass = 0.6f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        // Sans surface en dessous, le livre flotte (et reste là où on le lâche) au lieu de tomber par terre.
        rb.isKinematic = !surUneSurface;

        var grab = racine.AddComponent<XRGrabInteractable>();
        grab.useDynamicAttach = true;
        // Seul le collider principal sert à attraper : les zones à toucher ont leur propre rôle.
        grab.colliders.Clear();
        grab.colliders.Add(collider);

        // --- Zones à toucher du doigt ----------------------------------------------------------
        var tailleZonePage = new Vector3(0.08f, 0.03f, 0.28f);
        livre.zonePageSuivante = CreerZone(racine.transform, livre, "Zone Page Suivante", new Vector3(-0.18f, k_HauteurPage, 0f), tailleZonePage, ZoneTournerPage.Action.PageSuivante);
        livre.zonePagePrecedente = CreerZone(racine.transform, livre, "Zone Page Precedente", new Vector3(0.18f, k_HauteurPage, 0f), tailleZonePage, ZoneTournerPage.Action.PagePrecedente);
        livre.zoneCouverture = CreerZone(racine.transform, livre, "Zone Couverture", new Vector3(-0.11f, 0.075f, 0f), new Vector3(0.2f, 0.03f, 0.28f), ZoneTournerPage.Action.OuvrirFermer);
        livre.zonePageSuivante.SetActive(false);
        livre.zonePagePrecedente.SetActive(false);

        // Textes cachés tant que le livre est fermé (LivreVR s'en occupe aussi en jeu).
        livre.texteGauche.enabled = false;
        livre.texteRecto.enabled = false;
        livre.texteVerso.enabled = false;
        livre.texteSousFeuille.enabled = false;

        // --- Placement dans la scène ---------------------------------------------------------
        racine.transform.SetPositionAndRotation(position, OrientationVersJoueur(position));

        EditorSceneManager.MarkSceneDirty(racine.scene);
        Selection.activeGameObject = racine;
        SceneView.FrameLastActiveSceneView();

        Debug.Log($"[Livre VR] Livre placé en {position}. " +
                  (surUneSurface ? "Posé sur une surface." : "Aucune surface trouvée : le livre flotte (Rigidbody kinematic).") +
                  " En Play : O = ouvrir/fermer, flèches gauche/droite = tourner les pages.");
    }

    [MenuItem("Tools/Livre VR/Exporter pour le groupe (.unitypackage)")]
    static void Exporter()
    {
        const string cheminPrefab = "Assets/livre/Livre VR.prefab";

        // Enregistre le livre de la scène comme prefab réutilisable.
        var livre = GameObject.Find(k_NomLivre);
        if (livre != null)
            PrefabUtility.SaveAsPrefabAssetAndConnect(livre, cheminPrefab, InteractionMode.UserAction);
        else if (AssetDatabase.LoadAssetAtPath<GameObject>(cheminPrefab) == null)
        {
            EditorUtility.DisplayDialog("Livre VR", "Place d'abord le livre dans la scène (Tools > Livre VR > Placer le livre dans la scène).", "OK");
            return;
        }

        // Le package contient tout le dossier Assets/livre : modèle, scripts et prefab.
        var cheminPackage = System.IO.Path.GetFullPath("LivreVR.unitypackage");
        AssetDatabase.ExportPackage("Assets/livre", cheminPackage, ExportPackageOptions.Recurse);
        EditorUtility.RevealInFinder(cheminPackage);
        Debug.Log("[Livre VR] Package exporté : " + cheminPackage);
    }

    static TextMeshPro CreerTexte(string nom, Vector3 positionModele, Transform parent, string texte)
    {
        var go = new GameObject(nom);
        // Couché sur la page, face vers le haut, lisible par le lecteur placé côté +Z.
        go.transform.SetPositionAndRotation(positionModele, Quaternion.LookRotation(Vector3.down, Vector3.back));
        go.transform.SetParent(parent, true);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.rectTransform.sizeDelta = k_TailleTexte;
        tmp.text = texte;
        tmp.color = k_CouleurEncre;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.03f;
        tmp.fontSizeMax = 0.25f;
        return tmp;
    }

    static GameObject CreerZone(Transform parent, LivreVR livre, string nom, Vector3 surface, Vector3 taille, ZoneTournerPage.Action action)
    {
        // Le pivot de la zone est sur la surface à toucher : le doigt entre par le haut de la boîte
        // et la page est validée quand il atteint la surface.
        var go = new GameObject(nom);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = surface;

        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, taille.y * 0.5f, 0f);
        box.size = taille;

        go.AddComponent<XRSimpleInteractable>();

        var poke = go.AddComponent<XRPokeFilter>();
        poke.pokeConfiguration = new PokeThresholdDatumProperty(new PokeThresholdData
        {
            pokeDirection = PokeAxis.NegativeY, // le doigt appuie vers le bas
            enablePokeAngleThreshold = false,
        });

        var zone = go.AddComponent<ZoneTournerPage>();
        zone.livre = livre;
        zone.action = action;
        return go;
    }

    static Transform Trouver(Transform racine, string nom)
    {
        foreach (var t in racine.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == nom)
                return t;
        }
        return null;
    }

    static Vector3 TrouverPosition(out bool surUneSurface)
    {
        var cam = Camera.main;
        var origine = cam != null ? cam.transform.position : new Vector3(0f, 1.6f, 0f);
        var avant = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
        if (avant.sqrMagnitude < 0.01f)
            avant = Vector3.forward;

        var cible = origine + avant * 0.6f;

        // Cherche une table (ou toute surface) sous ce point, mais pas le sol.
        Physics.SyncTransforms();
        if (Physics.Raycast(new Vector3(cible.x, origine.y + 0.3f, cible.z), Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)
            && hit.point.y > 0.3f)
        {
            surUneSurface = true;
            return hit.point + Vector3.up * 0.01f;
        }

        surUneSurface = false;
        return new Vector3(cible.x, 1.0f, cible.z);
    }

    static Quaternion OrientationVersJoueur(Vector3 positionLivre)
    {
        var cam = Camera.main;
        if (cam == null)
            return Quaternion.identity;

        // Le côté +Z du livre (bas des pages) doit faire face au joueur.
        var versJoueur = Vector3.ProjectOnPlane(cam.transform.position - positionLivre, Vector3.up);
        return versJoueur.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(versJoueur.normalized, Vector3.up) : Quaternion.identity;
    }
}
