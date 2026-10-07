using System.Linq;
using Oculus.Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

/// <summary>
/// Construit dans la scène LivreTest :
/// - le rig VR Meta (casque + mains + interactions), copié depuis ScenePrincipal (qui n'est pas modifiée) ;
/// - le menu de départ (Start / Options / Quitter) façon grimoire : parchemin, cercle magique doré, glyphes,
///   utilisable au doigt (poke) et au rayon + pincement ;
/// - le message mystérieux affiché après Start.
/// Relancer l'outil refait le menu et le message (le rig n'est ajouté qu'une fois).
/// </summary>
static class MenuDepartSetup
{
    const int k_Version = 2; // augmente ce numéro pour reproposer la reconstruction du menu
    const string k_SceneLivre = "LivreTest";
    const string k_ScenePrincipale = "Assets/01_Scene/ScenePrincipal.unity";
    const string k_NomMenu = "Menu Depart";
    const string k_NomMessage = "Message Mystere";
    const string k_DejaPropose = "MenuDepartSetup.DejaPropose";
    static readonly string[] k_RigACopier = { "[BuildingBlock] Camera Rig", "[BuildingBlock] Passthrough" };
    static readonly string[] k_RigARetirer = { "CameraViewerManagerPrefab", "ReturnToStartScene" };

    // Modèles officiels du Meta Interaction SDK (mêmes que "Add Poke/Ray Interaction to Canvas").
    const string k_GuidPokeCanvas = "4db41829582c7d24f80ee9603868dd67";
    const string k_GuidRayCanvas = "8369d93f7b6b99742bbea0649a41b7b1";

    const string k_DossierMenu = "Assets/livre/Menu";
    const string k_DossierImages = k_DossierMenu + "/Images";
    const string k_DossierPolices = k_DossierMenu + "/Polices";
    const string k_DossierSymboles = "Assets/livre/Pages/symboles";

    const string k_TexteMessage =
        "On dit que la magie est un don,\nréservé à ceux qui naissent sorciers.\n\n" +
        "<size=120%>C'est un mensonge.</size>\n\n" +
        "Quelque part ici repose un grimoire oublié.\n" +
        "Ceux qui le trouvent découvrent le secret :\n" +
        "la magie ne se reçoit pas... elle se dessine.";
    const string k_TexteAppel = "Trouve le livre.";

    // Palette : encre brune, rouge de la couverture, or des glyphes.
    static readonly Color k_Encre = new Color32(52, 34, 20, 255);
    static readonly Color k_EncreRouge = new Color32(150, 40, 30, 255);
    static readonly Color k_Or = new Color32(232, 190, 100, 255);
    static readonly Color k_Clair = new Color32(246, 234, 204, 255);

    // --- Déclenchement ---------------------------------------------------------------------------

    [InitializeOnLoadMethod]
    static void ProposerAutomatiquement()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            var scene = SceneManager.GetActiveScene();
            if (scene.name != k_SceneLivre || EstAJour(scene) || SessionState.GetInt(k_DejaPropose, 0) >= k_Version)
                return;
            SessionState.SetInt(k_DejaPropose, k_Version); // une seule proposition par session Unity
            Creer();
        };
    }

    static bool EstAJour(Scene scene)
    {
        var menu = TrouverRacine(scene, k_NomMenu);
        return menu != null && menu.transform.Find("Cercle Magique") != null;
    }

    [MenuItem("Tools/Livre VR/Créer le menu de départ")]
    static void Creer()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Menu de départ", "Quitte d'abord le mode Play.", "OK");
            return;
        }
        if (scene.name != k_SceneLivre)
        {
            EditorUtility.DisplayDialog("Menu de départ", $"Ouvre d'abord la scène {k_SceneLivre} : le menu ne se crée que dans celle-ci.", "OK");
            return;
        }

        var ancienMenu = TrouverRacine(scene, k_NomMenu);
        var titre = ancienMenu != null ? "Remplacer le menu de départ par la nouvelle version (style grimoire) ?" : "Créer le menu de départ ?";
        if (!EditorUtility.DisplayDialog("Menu de départ",
                $"{titre}\n\nScène : « {k_SceneLivre} » uniquement.\n" +
                "• Menu en parchemin avec cercle magique doré, police Cinzel et vos glyphes\n" +
                "• Message mystérieux après Start\n" +
                (ancienMenu == null ? "• Rig VR (casque + mains) copié depuis ScenePrincipal, ancienne Main Camera désactivée\n" : "• Le rig VR déjà présent n'est pas touché\n") +
                "\nLe livre n'est pas touché. La scène sera sauvegardée.", "Oui", "Pas maintenant"))
            return;

        // Ancienne version : on la remplace entièrement.
        foreach (var nom in new[] { k_NomMenu, k_NomMessage })
        {
            var ancien = TrouverRacine(scene, nom);
            if (ancien != null)
                Undo.DestroyObjectImmediate(ancien);
        }

        var tete = AjouterRig(scene);
        AjouterEventSystem();
        var polices = ChargerPolices();
        var message = CreerMessage(polices);
        CreerMenu(tete, message, polices);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Menu de départ] Menu (style grimoire) et message mystérieux créés dans " + k_SceneLivre + ".");
    }

    // --- Rig VR --------------------------------------------------------------------------------

    static Transform AjouterRig(Scene sceneLivre)
    {
        var dejaLa = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(c => c != null && c.GetType().Name == "OVRCameraRig" && c.gameObject.scene == sceneLivre);
        if (dejaLa == null)
        {
            var principale = SceneManager.GetSceneByPath(k_ScenePrincipale);
            var ouverteIci = !principale.isLoaded;
            if (ouverteIci)
                principale = EditorSceneManager.OpenScene(k_ScenePrincipale, OpenSceneMode.Additive);

            var aCopier = k_RigACopier.Select(n => TrouverRacine(principale, n)).Where(g => g != null).ToArray();
            if (aCopier.Length == 0)
                Debug.LogWarning("[Menu de départ] Rig introuvable dans ScenePrincipal : les mains ne marcheront pas dans le casque.");
            else
            {
                // Copier-coller comme dans l'éditeur : garde les liens entre les objets du rig.
                Selection.objects = aCopier;
                Unsupported.CopyGameObjectsToPasteboard();
                Selection.objects = new Object[0];
                SceneManager.SetActiveScene(sceneLivre);
                Unsupported.PasteGameObjectsFromPasteboard();

                foreach (var go in Selection.gameObjects)
                {
                    go.name = go.name.Replace("(Clone)", "").Trim();
                    if (go.scene != sceneLivre && go.transform.parent == null)
                        SceneManager.MoveGameObjectToScene(go, sceneLivre);
                    foreach (var nom in k_RigARetirer)
                    {
                        var aRetirer = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == nom);
                        if (aRetirer != null)
                            Undo.DestroyObjectImmediate(aRetirer.gameObject);
                    }
                }
            }

            if (ouverteIci)
                EditorSceneManager.CloseScene(principale, true);
            SceneManager.SetActiveScene(sceneLivre);

            // L'ancienne caméra ferait doublon avec celle du casque.
            var ancienneCamera = TrouverRacine(sceneLivre, "Main Camera");
            if (ancienneCamera != null)
            {
                Undo.RecordObject(ancienneCamera, "Menu de départ");
                ancienneCamera.SetActive(false);
            }
        }

        return sceneLivre.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == "CenterEyeAnchor");
    }

    static void AjouterEventSystem()
    {
        var es = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (es == null)
        {
            var go = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(go, "Menu de départ");
            es = go.AddComponent<EventSystem>();
        }
        // Module du Meta SDK : transmet le doigt et le rayon des mains aux canvas.
        if (es.GetComponent<PointableCanvasModule>() == null)
            Undo.AddComponent<PointableCanvasModule>(es.gameObject);
    }

    // --- Polices -------------------------------------------------------------------------------

    struct Polices
    {
        public TMP_FontAsset titre;   // Cinzel Decorative Bold
        public TMP_FontAsset texte;   // Cinzel Decorative Regular
    }

    static Polices ChargerPolices()
    {
        return new Polices
        {
            titre = PoliceTMP("CinzelDecorative-Bold"),
            texte = PoliceTMP("CinzelDecorative-Regular"),
        };
    }

    /// <summary>Crée (une seule fois) l'asset de police TextMeshPro à partir du .ttf téléchargé.</summary>
    static TMP_FontAsset PoliceTMP(string nom)
    {
        var chemin = $"{k_DossierPolices}/{nom} SDF.asset";
        var existante = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(chemin);
        if (existante != null)
            return existante;

        var ttf = AssetDatabase.LoadAssetAtPath<Font>($"{k_DossierPolices}/{nom}.ttf");
        if (ttf == null)
        {
            Debug.LogWarning($"[Menu de départ] Police {nom}.ttf introuvable : police par défaut utilisée.");
            return TMP_Settings.defaultFontAsset;
        }

        var police = TMP_FontAsset.CreateFontAsset(ttf, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        police.name = nom + " SDF";
        AssetDatabase.CreateAsset(police, chemin);
        police.atlasTextures[0].name = nom + " Atlas";
        AssetDatabase.AddObjectToAsset(police.atlasTextures[0], police);
        police.material.name = nom + " Material";
        AssetDatabase.AddObjectToAsset(police.material, police);
        // Les accents et la ponctuation française, si la police les a.
        police.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
        EditorUtility.SetDirty(police);
        AssetDatabase.SaveAssets();
        return police;
    }

    // --- Menu ------------------------------------------------------------------------------------

    static void CreerMenu(Transform tete, MessageMystere message, Polices polices)
    {
        var canvas = CreerCanvas(k_NomMenu, new Vector2(500f, 580f));
        canvas.gameObject.AddComponent<CanvasGroup>();
        var menu = canvas.gameObject.AddComponent<MenuDepart>();
        menu.tete = tete;
        menu.message = message;

        // Derrière le parchemin : halo doré + grand cercle magique qui tourne et dépasse du parchemin.
        var halo = CreerRawImage(canvas.transform, "Halo", Image("lueur_ronde"), Vector2.zero, new Vector2(980f, 980f));
        halo.color = new Color(1f, 1f, 1f, 0.45f);
        var cercle = CreerRawImage(canvas.transform, "Cercle Magique", Symbole("glyphe_final_or_cercle"), Vector2.zero, new Vector2(800f, 800f));
        var anim = cercle.gameObject.AddComponent<CercleMagique>();
        anim.pulsation = cercle;
        anim.opaciteMin = 0.55f;
        anim.opaciteMax = 0.95f;

        CreerRawImage(canvas.transform, "Parchemin", Image("parchemin"), Vector2.zero, new Vector2(500f, 580f));

        // Panneau principal
        var principal = CreerPanneau(canvas.transform, "Panneau Principal");
        CreerTexte(principal, "Titre", "Magical\nCircle", polices.titre, 52f, k_Encre, new Vector2(0f, 186f), new Vector2(420f, 140f)).lineSpacing = -18f;
        CreerTexte(principal, "Sous-titre", "~ le grimoire des cercles ~", polices.texte, 19f, k_EncreRouge, new Vector2(0f, 104f), new Vector2(420f, 30f));
        CreerRawImage(principal, "Separateur", Image("separateur"), new Vector2(0f, 80f), new Vector2(320f, 26f));
        Brancher(CreerBouton(principal, "Bouton Start", "Start", Symbole("glyphe_lumiere_or"), polices.titre, new Vector2(0f, 10f)), menu.Commencer);
        Brancher(CreerBouton(principal, "Bouton Options", "Options", Symbole("glyphe_terre_or"), polices.titre, new Vector2(0f, -84f)), menu.OuvrirOptions);
        Brancher(CreerBouton(principal, "Bouton Quitter", "Quitter", Symbole("glyphe_eau_or"), polices.titre, new Vector2(0f, -178f)), menu.Quitter);
        var aide = CreerTexte(principal, "Aide", "touche ou pince pour choisir", polices.texte, 14f, k_Encre, new Vector2(0f, -248f), new Vector2(420f, 24f));
        aide.alpha = 0.6f;

        // Panneau options
        var options = CreerPanneau(canvas.transform, "Panneau Options");
        CreerTexte(options, "Titre", "Options", polices.titre, 50f, k_Encre, new Vector2(0f, 186f), new Vector2(420f, 80f));
        CreerRawImage(options, "Separateur", Image("separateur"), new Vector2(0f, 130f), new Vector2(320f, 26f));
        menu.texteVolume = CreerTexte(options, "Texte Volume", "Volume : 100 %", polices.texte, 28f, k_Encre, new Vector2(0f, 60f), new Vector2(420f, 44f));
        menu.sliderVolume = CreerSlider(options, new Vector2(0f, -4f));
        Brancher(CreerBouton(options, "Bouton Retour", "Retour", Symbole("glyphe_vent_or"), polices.titre, new Vector2(0f, -150f)), menu.RetourMenu);
        options.gameObject.SetActive(false);

        menu.panneauPrincipal = principal.gameObject;
        menu.panneauOptions = options.gameObject;

        CreerPoussiere(canvas.transform, new Vector3(0.62f, 0.66f, 0.12f), 14f);

        // Interactions Meta : toucher du doigt + rayon / pincement.
        AjouterInteractionMeta(canvas, k_GuidPokeCanvas, "ISDK_PokeCanvasInteraction");
        AjouterInteractionMeta(canvas, k_GuidRayCanvas, "ISDK_RayCanvasInteraction");

        // Position de départ dans l'éditeur (en jeu, le menu se place tout seul devant le joueur).
        canvas.transform.position = tete != null ? tete.position + Vector3.forward * menu.distance : new Vector3(0f, 1.3f, 0.55f);
    }

    static MessageMystere CreerMessage(Polices polices)
    {
        var canvas = CreerCanvas(k_NomMessage, new Vector2(1000f, 640f));
        canvas.gameObject.AddComponent<CanvasGroup>();

        CreerRawImage(canvas.transform, "Voile", Image("voile_message"), Vector2.zero, new Vector2(1300f, 800f));
        var cercle = CreerRawImage(canvas.transform, "Cercle Magique", Symbole("glyphe_final_or_cercle"), new Vector2(0f, 10f), new Vector2(720f, 720f));
        var anim = cercle.gameObject.AddComponent<CercleMagique>();
        anim.vitesse = 4f;
        anim.pulsation = cercle;
        anim.opaciteMin = 0.12f;
        anim.opaciteMax = 0.3f;

        var texte = CreerTexte(canvas.transform, "Texte", k_TexteMessage, polices.texte, 30f, k_Clair, new Vector2(0f, 60f), new Vector2(900f, 420f));
        texte.lineSpacing = 6f;
        CreerRawImage(canvas.transform, "Separateur", Image("separateur"), new Vector2(0f, -178f), new Vector2(300f, 24f)).color = k_Or;
        CreerTexte(canvas.transform, "Appel", k_TexteAppel, polices.titre, 52f, k_Or, new Vector2(0f, -240f), new Vector2(900f, 80f));

        CreerPoussiere(canvas.transform, new Vector3(1.0f, 0.6f, 0.15f), 18f);
        canvas.transform.position = new Vector3(0f, 1.5f, 1.2f);
        return canvas.gameObject.AddComponent<MessageMystere>();
    }

    static void AjouterInteractionMeta(Canvas canvas, string guid, string nom)
    {
        var modele = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
        if (modele == null)
        {
            Debug.LogWarning($"[Menu de départ] Modèle Meta {nom} introuvable : cette interaction n'a pas été ajoutée.");
            return;
        }

        var go = Object.Instantiate(modele, canvas.transform, false);
        go.name = nom;
        var rt = go.GetComponent<RectTransform>();
        rt.localPosition = Vector3.zero;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;
        Etirer(rt);
        go.GetComponentInChildren<PointableCanvas>(true).InjectCanvas(canvas);
    }

    // --- Éléments ------------------------------------------------------------------------------

    static Canvas CreerCanvas(string nom, Vector2 taille)
    {
        var go = new GameObject(nom, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Menu de départ");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        go.AddComponent<GraphicRaycaster>();
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = taille;
        rt.localScale = Vector3.one * 0.001f; // 1 unité = 1 mm
        return canvas;
    }

    static RectTransform CreerPanneau(Transform parent, string nom)
    {
        var go = new GameObject(nom, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        Etirer(rt);
        return rt;
    }

    static RawImage CreerRawImage(Transform parent, string nom, Texture texture, Vector2 position, Vector2 taille)
    {
        var go = new GameObject(nom, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        image.rectTransform.anchoredPosition = position;
        image.rectTransform.sizeDelta = taille;
        return image;
    }

    static TextMeshProUGUI CreerTexte(Transform parent, string nom, string texte, TMP_FontAsset police, float taille, Color couleur, Vector2 position, Vector2 dimensions)
    {
        var go = new GameObject(nom, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (police != null)
            tmp.font = police;
        tmp.text = texte;
        tmp.fontSize = taille;
        tmp.color = couleur;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        tmp.rectTransform.anchoredPosition = position;
        tmp.rectTransform.sizeDelta = dimensions;
        return tmp;
    }

    static Button CreerBouton(Transform parent, string nom, string texte, Texture glyphe, TMP_FontAsset police, Vector2 position)
    {
        var go = new GameObject(nom, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(360f, 84f); // 36 x 8,4 cm : facile à toucher du doigt

        // Halo doré derrière (allumé au survol par BoutonMagique).
        var lueur = CreerRawImage(go.transform, "Lueur", Image("bouton_lueur"), Vector2.zero, new Vector2(400f, 104f));
        lueur.color = new Color(1f, 1f, 1f, 0f);

        // Cadre à l'encre : c'est lui qui reçoit le doigt / le rayon.
        var cadre = CreerRawImage(go.transform, "Cadre", Image("bouton"), Vector2.zero, new Vector2(360f, 84f));
        cadre.raycastTarget = true;

        var icone = CreerRawImage(go.transform, "Glyphe", glyphe, new Vector2(-128f, 0f), new Vector2(58f, 58f));
        CreerTexte(go.transform, "Texte", texte, police, 32f, k_Encre, new Vector2(20f, 0f), new Vector2(240f, 70f));

        var bouton = go.AddComponent<Button>();
        bouton.targetGraphic = cadre;
        bouton.transition = Selectable.Transition.ColorTint;
        var couleurs = bouton.colors;
        couleurs.normalColor = Color.white;
        couleurs.highlightedColor = new Color(1f, 0.95f, 0.82f);
        couleurs.pressedColor = new Color(1f, 0.85f, 0.55f);
        couleurs.selectedColor = Color.white;
        couleurs.fadeDuration = 0.1f;
        bouton.colors = couleurs;
        var nav = bouton.navigation;
        nav.mode = Navigation.Mode.None;
        bouton.navigation = nav;

        var magie = go.AddComponent<BoutonMagique>();
        magie.lueur = lueur;
        magie.glyphe = icone.rectTransform;
        return bouton;
    }

    static Slider CreerSlider(Transform parent, Vector2 position)
    {
        var go = new GameObject("Slider Volume", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(320f, 60f);

        // Zone touchable invisible (plus haute que le trait, pour les doigts).
        var zone = go.AddComponent<Image>();
        zone.color = new Color(1f, 1f, 1f, 0f);

        var trait = new GameObject("Trait", typeof(RectTransform)).AddComponent<Image>();
        trait.transform.SetParent(go.transform, false);
        trait.color = new Color(k_Encre.r, k_Encre.g, k_Encre.b, 0.5f);
        trait.raycastTarget = false;
        Etirer(trait.rectTransform);
        trait.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        trait.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        trait.rectTransform.sizeDelta = new Vector2(0f, 6f);

        var zoneRemplissage = new GameObject("Remplissage", typeof(RectTransform));
        zoneRemplissage.transform.SetParent(go.transform, false);
        var zr = (RectTransform)zoneRemplissage.transform;
        Etirer(zr);
        zr.anchorMin = new Vector2(0f, 0.5f);
        zr.anchorMax = new Vector2(1f, 0.5f);
        zr.sizeDelta = new Vector2(0f, 6f);
        var remplissage = new GameObject("Or", typeof(RectTransform)).AddComponent<Image>();
        remplissage.transform.SetParent(zr, false);
        remplissage.color = new Color32(176, 128, 40, 255);
        remplissage.raycastTarget = false;
        Etirer(remplissage.rectTransform);

        var zonePoignee = new GameObject("Zone Poignee", typeof(RectTransform));
        zonePoignee.transform.SetParent(go.transform, false);
        var zp = (RectTransform)zonePoignee.transform;
        Etirer(zp);
        var poignee = new GameObject("Poignee", typeof(RectTransform));
        poignee.transform.SetParent(zp, false);
        var pr = (RectTransform)poignee.transform;
        pr.sizeDelta = new Vector2(56f, 56f);
        var lueur = CreerRawImage(pr, "Lueur", Image("lueur_ronde"), Vector2.zero, new Vector2(90f, 90f));
        lueur.color = new Color(1f, 1f, 1f, 0.8f);
        var glyphe = CreerRawImage(pr, "Glyphe", Symbole("glyphe_lumiere_or_cercle"), Vector2.zero, new Vector2(56f, 56f));

        var slider = go.AddComponent<Slider>();
        slider.fillRect = remplissage.rectTransform;
        slider.handleRect = pr;
        slider.targetGraphic = glyphe;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;
        var nav = slider.navigation;
        nav.mode = Navigation.Mode.None;
        slider.navigation = nav;
        return slider;
    }

    /// <summary>Fine poussière dorée qui flotte et monte doucement autour du panneau.</summary>
    static void CreerPoussiere(Transform parent, Vector3 tailleZone, float parSeconde)
    {
        var go = new GameObject("Poussiere Doree");
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * 1000f; // annule l'échelle du canvas : on travaille en mètres
        go.transform.localPosition = new Vector3(0f, 0f, -20f);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.005f, 0.02f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.011f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.95f, 0.75f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 120;
        main.prewarm = true;
        main.useUnscaledTime = true;

        var emission = ps.emission;
        emission.rateOverTime = parSeconde;

        var forme = ps.shape;
        forme.shapeType = ParticleSystemShapeType.Box;
        forme.scale = tailleZone;

        var vitesse = ps.velocityOverLifetime;
        vitesse.enabled = true;
        vitesse.space = ParticleSystemSimulationSpace.Local;
        vitesse.y = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);
        vitesse.x = new ParticleSystem.MinMaxCurve(-0.005f, 0.005f);
        vitesse.z = new ParticleSystem.MinMaxCurve(-0.005f, 0.005f);

        var couleur = ps.colorOverLifetime;
        couleur.enabled = true;
        var degrade = new Gradient();
        degrade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.25f), new GradientAlphaKey(0.6f, 0.7f), new GradientAlphaKey(0f, 1f) });
        couleur.color = degrade;

        var rendu = go.GetComponent<ParticleSystemRenderer>();
        var shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Standard Unlit");
        var materiau = new Material(shader) { name = "Poussiere Doree" };
        materiau.mainTexture = Image("particule");
        rendu.sharedMaterial = materiau;
        rendu.renderMode = ParticleSystemRenderMode.Billboard;
    }

    // --- Outils ------------------------------------------------------------------------------

    static Texture2D Image(string nom) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{k_DossierImages}/{nom}.png");

    static Texture2D Symbole(string nom) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{k_DossierSymboles}/{nom}.png");

    static void Brancher(Button bouton, UnityEngine.Events.UnityAction action)
    {
        UnityEventTools.AddPersistentListener(bouton.onClick, action);
    }

    static void Etirer(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }

    static GameObject TrouverRacine(Scene scene, string nom)
    {
        return scene.IsValid() && scene.isLoaded ? scene.GetRootGameObjects().FirstOrDefault(g => g.name == nom) : null;
    }
}
