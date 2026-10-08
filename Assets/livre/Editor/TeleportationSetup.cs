using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.DistanceReticles;
using Oculus.Interaction.Input;
using Oculus.Interaction.Locomotion;
using Oculus.Interaction.Surfaces;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Téléportation à la main dans la scène LivreTest, sur le modèle du tuto "How to Teleport with Hand Tracking"
/// (3D Portal), refait avec le Meta Interaction SDK utilisé par le projet :
///  1. le sol devient une zone de téléportation (équivalent "Teleportation Area") ;
///  2. chaque main reçoit l'interacteur officiel Meta "HandTeleportInteractorGroup" :
///     geste en L (pouce + index tendus) = le rayon apparaît, pincement = téléportation ;
///  3. l'interacteur est ajouté au groupe d'interactions de la main, avant le rayon normal
///     (équivalent "XR Interaction Group" : un seul rayon visible à la fois) ;
///  4. l'arc est fin et bleu, invisible quand on vise un endroit interdit (réglages du tuto).
/// Les micro-gestes du pouce (autre système de téléportation de Meta) sont désactivés pour ne pas doublonner.
/// Menu : Tools > Livre VR > Ajouter la téléportation. Relancer le menu refait tout proprement.
/// </summary>
static class TeleportationSetup
{
    const string k_SceneLivre = "LivreTest";
    const string k_NomRacine = "Teleportation";
    const string k_NomMicroGestes = "MicroGesturesLocomotionHandInteractorGroup";
    const string k_NomTeleportMain = "HandTeleportInteractorGroup";
    const string k_DejaPropose = "TeleportationSetup.DejaPropose.v2";

    // Modèles officiels du Meta Interaction SDK (ceux des Quick Actions).
    const string k_GuidSolTeleportable = "70cf16e50c457a54cad0b0a504b7d646"; // ISDK_TeleportInteraction
    const string k_GuidTeleportMain = "1b3597a7837cb3545b4a4f6e30856067";    // HandTeleportInteractorGroup

    // Couleurs de l'arc (comme dans le tuto) : bleu quand c'est valide, invisible sinon.
    static readonly Color k_Bleu = new Color(0.25f, 0.65f, 1f, 1f);
    static readonly Color k_Invisible = new Color(1f, 1f, 1f, 0f);

    [InitializeOnLoadMethod]
    static void ProposerAutomatiquement()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            var scene = SceneManager.GetActiveScene();
            if (scene.name != k_SceneLivre || TrouverRacine(scene, k_NomRacine) != null || SessionState.GetBool(k_DejaPropose, false))
                return;
            SessionState.SetBool(k_DejaPropose, true); // une seule proposition par session Unity
            Ajouter();
        };
    }

    [MenuItem("Tools/Livre VR/Ajouter la téléportation")]
    static void Ajouter()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.name != k_SceneLivre)
        {
            EditorUtility.DisplayDialog("Téléportation", $"Quitte le mode Play et ouvre la scène {k_SceneLivre}.", "OK");
            return;
        }

        // Ce qu'il faut trouver dans le rig Meta de la scène.
        MonoBehaviour locomoteur = Object.FindFirstObjectByType<FirstPersonLocomotor>(FindObjectsInactive.Include);
        if (locomoteur == null)
            locomoteur = Object.FindFirstObjectByType<PlayerLocomotor>(FindObjectsInactive.Include);
        var hmd = Object.FindFirstObjectByType<Hmd>(FindObjectsInactive.Include);
        var microGestes = TousLesObjets(scene).Where(t => t.name == k_NomMicroGestes).ToList();
        var modeleMain = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(k_GuidTeleportMain));
        var modeleSol = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(k_GuidSolTeleportable));
        if (locomoteur == null || hmd == null || microGestes.Count == 0 || modeleMain == null || modeleSol == null)
        {
            EditorUtility.DisplayDialog("Téléportation",
                "Il manque un élément du rig VR Meta dans la scène (Locomotor, casque ou groupes de mains), " +
                "ou un modèle du Meta SDK. Rien n'a été modifié.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Téléportation",
                $"Ajouter la téléportation à la main dans « {k_SceneLivre} » ?\n\n" +
                "• Tout le sol devient une zone de téléportation\n" +
                "• Geste en L (pouce + index tendus) = le rayon apparaît, pincer = se téléporter\n" +
                "• Rayon bleu et fin, invisible si on vise un endroit interdit\n" +
                "• Les micro-gestes du pouce sont désactivés (pas supprimés)\n" +
                "• Touche T pour tester sans casque\n\n" +
                "Le livre et le menu ne sont pas touchés. La scène sera sauvegardée (Ctrl+Z pour annuler).", "Oui", "Pas maintenant"))
            return;

        Undo.SetCurrentGroupName("Téléportation");
        var groupeUndo = Undo.GetCurrentGroup();

        // Si l'outil a déjà été lancé : on enlève l'ancienne version pour repartir propre.
        var ancienne = TrouverRacine(scene, k_NomRacine);
        if (ancienne != null)
            Undo.DestroyObjectImmediate(ancienne);
        foreach (var vieux in TousLesObjets(scene).Where(t => t.name == k_NomTeleportMain).ToList())
            Undo.DestroyObjectImmediate(vieux.gameObject);

        // 1. Le sol téléportable (+ sol physique invisible pour le FirstPersonLocomotor).
        var origine = new SerializedObject(locomoteur).FindProperty("_playerOrigin").objectReferenceValue as Transform;
        var racine = new GameObject(k_NomRacine);
        Undo.RegisterCreatedObjectUndo(racine, "Téléportation");
        racine.transform.position = new Vector3(0f, origine != null ? origine.position.y : 0f, 0f);
        CreerSolTeleportable(modeleSol, racine.transform);
        CreerSolPhysique(racine.transform);

        // 2. et 3. Une téléportation au geste en L sur chaque main, à la place des micro-gestes.
        var mains = 0;
        foreach (var micro in microGestes)
        {
            if (AjouterSurLaMain(modeleMain, micro, hmd, (ILocomotionEventHandler)locomoteur))
                mains++;
        }

        // Test au clavier (touche T).
        var tete = TousLesObjets(scene).FirstOrDefault(t => t.name == "CenterEyeAnchor");
        var test = racine.AddComponent<TeleportationClavier>();
        test.locomoteur = locomoteur;
        test.tete = tete;
        test.hauteurSol = racine.transform.position.y;

        Undo.CollapseUndoOperations(groupeUndo);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Téléportation] Sol téléportable ajouté, téléportation au geste en L installée sur {mains} main(s), Locomotor « {locomoteur.name} ».");
    }

    // --- Main ----------------------------------------------------------------------------------

    /// <summary>
    /// Fait ce que fait l'outil Meta "Add Teleport Interaction" (mode "geste en L") pour une main :
    /// ajoute le modèle, le relie à la main, au casque, au groupe d'interactions et au Locomotor.
    /// </summary>
    static bool AjouterSurLaMain(GameObject modele, Transform microGestes, Hmd hmd, ILocomotionEventHandler locomoteur)
    {
        var refMain = microGestes.GetComponentInChildren<HandRef>(true);
        var main = refMain != null ? new SerializedObject(refMain).FindProperty("_hand").objectReferenceValue as IHand : null;
        var parent = microGestes.parent;
        if (main == null || parent == null)
        {
            Debug.LogWarning($"[Téléportation] Main introuvable pour « {microGestes.name} » : ignorée.");
            return false;
        }

        var go = (GameObject)Object.Instantiate(modele, parent, false);
        go.name = k_NomTeleportMain;
        Undo.RegisterCreatedObjectUndo(go, "Téléportation");
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        go.GetComponent<HandRef>().InjectHand(main);
        go.GetComponent<HmdRef>()?.InjectHmd(hmd);

        foreach (var connexion in go.GetComponentsInChildren<LocomotionEventsConnection>(true))
            connexion.InjectHandlers(new List<ILocomotionEventHandler> { locomoteur });

        RendreLArcCommeLeTuto(go);

        // Groupe d'interactions de la main : la téléportation passe avant le rayon normal.
        var groupe = parent.GetComponentInParent<InteractorGroup>(true);
        var interacteur = go.GetComponent<IInteractor>() as Object;
        if (groupe != null && interacteur != null)
        {
            Undo.RecordObject(groupe, "Téléportation");
            var liste = new SerializedObject(groupe).FindProperty("_interactors");
            var interacteurs = Enumerable.Range(0, liste.arraySize).Select(i => liste.GetArrayElementAtIndex(i).objectReferenceValue).ToList();
            var rayon = interacteurs.FindIndex(o => o != null && o.name.Contains("Ray"));
            interacteurs.Insert(rayon >= 0 ? rayon : interacteurs.Count, interacteur);
            groupe.InjectInteractors(interacteurs.OfType<IInteractor>().ToList());
            PrefabUtility.RecordPrefabInstancePropertyModifications(groupe);
        }

        // Un seul système de téléportation par main : on éteint les micro-gestes (sans les supprimer).
        Undo.RecordObject(microGestes.gameObject, "Téléportation");
        microGestes.gameObject.SetActive(false);
        PrefabUtility.RecordPrefabInstancePropertyModifications(microGestes.gameObject);
        return true;
    }

    /// <summary>Réglages de l'arc du tuto : fin, bleu (transparent au départ), invisible si la cible est interdite.</summary>
    static void RendreLArcCommeLeTuto(GameObject racine)
    {
        foreach (var tube in racine.GetComponentsInChildren<TubeRenderer>(true))
        {
            var so = new SerializedObject(tube);
            so.FindProperty("_radius").floatValue = 0.005f; // 1 cm d'épaisseur (largeur 0.01 du tuto)
            var degrade = new Gradient();
            degrade.SetKeys(
                new[] { new GradientColorKey(k_Bleu, 0f), new GradientColorKey(k_Bleu, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 1f) });
            so.FindProperty("_gradient").gradientValue = degrade;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        foreach (var arc in racine.GetComponentsInChildren<TeleportProceduralArcVisual>(true))
            arc.NoDestinationTint = k_Invisible;

        foreach (var reticule in racine.GetComponentsInChildren<TeleportReticleDrawer>(true))
        {
            var so = new SerializedObject(reticule);
            so.FindProperty("_acceptColor").colorValue = k_Bleu;
            so.FindProperty("_rejectColor").colorValue = k_Invisible;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // --- Sol -----------------------------------------------------------------------------------

    /// <summary>Reproduit le mode "Plane" de l'action Meta "Add Teleport Interaction" : tout le sol est téléportable.</summary>
    static void CreerSolTeleportable(GameObject modele, Transform parent)
    {
        var go = (GameObject)Object.Instantiate(modele, parent, false);
        go.name = "Sol Teleportable";
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        var plan = new GameObject("Plane");
        plan.transform.SetParent(go.transform, false);
        plan.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
        var surface = plan.AddComponent<PlaneSurface>();
        surface.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);

        var interactable = go.GetComponent<TeleportInteractable>();
        interactable.InjectSurface(surface);
        interactable.TieBreakerScore = 0;
        interactable.AllowTeleport = true;

        // En mode Plane, Meta retire les visuels de "point de téléportation" (anneau / flèche).
        var reticule = go.GetComponent<ReticleDataTeleport>();
        reticule.HideReticle = false;
        reticule.InjectOptionalMaterialPropertyBlockEditor(null);
        var visuels = go.transform.Find("Visuals");
        if (visuels != null)
            Object.DestroyImmediate(visuels.gameObject);
    }

    /// <summary>
    /// Sol physique invisible : le FirstPersonLocomotor a besoin d'un sol sous le joueur
    /// (sinon il bloque le déplacement). Invisible : en réalité mixte, on voit le vrai sol.
    /// </summary>
    static void CreerSolPhysique(Transform parent)
    {
        var go = new GameObject("Sol Physique (invisible)");
        go.transform.SetParent(parent, false);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(60f, 0.1f, 60f);
        box.center = new Vector3(0f, -0.05f, 0f); // le dessus est exactement à la hauteur du sol
    }

    // --- Outils --------------------------------------------------------------------------------

    static IEnumerable<Transform> TousLesObjets(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true));
    }

    static GameObject TrouverRacine(Scene scene, string nom)
    {
        return scene.GetRootGameObjects().FirstOrDefault(g => g.name == nom);
    }
}
