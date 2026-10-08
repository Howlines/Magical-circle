using System.Collections;
using Oculus.Interaction.Locomotion;
using UnityEngine;
using UnityEngine.InputSystem;

// Fin du jeu, déclenchée par le sort final (événement "Après le sort" du SpellCaster "ultimate").
//
// Déroulé :
//   1. on laisse l'effet du sort démarrer (attente réglable) ;
//   2. tout devient blanc progressivement (un quad blanc enfant de CenterEyeAnchor, juste devant les yeux) ;
//   3. pendant le blanc total, on change la scène sans que le joueur le voie :
//      - on arrête le locomoteur et on ramène le rig au bureau (la table virtuelle retombe sur la vraie) ;
//      - on désactive le passthrough, la fenêtre, les cibles et les interactors de téléportation ;
//      - on pose l'image du décompte à plat sur la table ;
//   4. le blanc s'estompe lentement, et la lueur du 7e bâton pulse tant que le jeu tourne.
//
// En éditeur : touche F pour déclencher la fin sans lancer le sort.
public class FinDuJeu : MonoBehaviour
{
    [Header("Flash blanc")]
    [SerializeField] private Renderer m_quadFlash;                  // quad enfant de CenterEyeAnchor, matériau Unlit transparent
    [SerializeField] private Color m_couleurFlash = Color.white;
    [SerializeField] private float m_attenteAvantBlanc = 0f;        // secondes pour laisser l'effet du sort démarrer
    [SerializeField] private float m_dureeMonteeBlanc = 1.5f;       // jusqu'au blanc total
    [SerializeField] private float m_dureePauseBlanc = 0.5f;        // temps passé en blanc total
    [SerializeField] private float m_dureeDescenteBlanc = 3f;       // retour progressif à l'image

    [Header("Retour au bureau")]
    [SerializeField] private TableCalibration m_calibration;        // donne la pose du rig et TableAnchor
    [SerializeField] private FirstPersonLocomotor m_locomoteur;     // sur PlayerController. Vide = cherché dans la scène

    [Header("Désactivés à la fin")]
    // [BuildingBlock] Passthrough, la fenêtre (m_window), les 3 ISDK_TeleportInteraction
    // et les interactors de téléportation des mains.
    [SerializeField] private GameObject[] m_objetsADesactiver;

    [Header("Image du décompte")]
    [SerializeField] private Renderer m_quadBatons;                 // quad avec matériau transparent (Unlit/Transparent)
    [SerializeField] private Renderer m_quadLueur;                  // quad avec matériau additif (Legacy Shaders/Particles/Additive)
    [SerializeField] private Texture m_textureBatons;               // decompte_7_batons.png
    [SerializeField] private Texture m_textureLueur;                // decompte_lueur_seule.png
    [SerializeField] private float m_largeurImage = 0.4f;           // largeur sur la table, en mètres (la hauteur suit les proportions du PNG)
    [SerializeField] private float m_hauteurAuDessusTable = 0.002f; // 2 mm : évite que l'image "clignote" avec le bois
    [SerializeField] private float m_decalageVersJoueur = 0.15f;    // en mètres, depuis le centre de TableAnchor vers le bord côté joueur

    [Header("Pulsation de la lueur")]
    [SerializeField] private Color m_couleurLueur = Color.white;
    [SerializeField] private float m_intensiteLueur = 1f;           // intensité moyenne
    [SerializeField] private float m_amplitudePulsation = 0.5f;     // de combien l'intensité monte et descend autour de la moyenne
    [SerializeField] private float m_vitessePulsation = 0.3f;       // pulsations par seconde (0.3 = une toutes les ~3 s)

    private bool m_lancee;              // garde-fou : la fin ne se joue qu'une fois
    private Material m_materielLueur;   // copie du matériau de la lueur, dont on fait varier la couleur
    private Material m_materielFlash;   // copie du matériau du flash, dont on fait varier la transparence

    private void Start()
    {
        PreparerFlash();

        // Les quads restent cachés jusqu'au blanc total, même s'ils sont actifs dans la scène.
        if (m_quadBatons != null) m_quadBatons.gameObject.SetActive(false);
        if (m_quadLueur != null) m_quadLueur.gameObject.SetActive(false);
    }

    // Branchée sur l'événement "Après le sort" du SpellCaster du glyphe final.
    public void Lancer()
    {
        if (m_lancee) return;
        m_lancee = true;
        StartCoroutine(SequenceDeFin());
    }

    private void Update()
    {
        // Touche F, uniquement dans l'éditeur Unity : tester la fin sans dessiner le sort.
        if (Application.isEditor && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) Lancer();

        // La lueur pulse : intensité = moyenne + amplitude × sinus du temps.
        if (m_materielLueur != null)
        {
            float intensite = m_intensiteLueur + m_amplitudePulsation * Mathf.Sin(Time.time * m_vitessePulsation * 2f * Mathf.PI);
            intensite = Mathf.Max(0f, intensite);
            // En additif, plus la couleur est forte, plus la lueur éclaire.
            // Le nom de la couleur dépend du shader : _BaseColor (URP), _TintColor (particules legacy), _Color (les autres).
            Color c = m_couleurLueur * intensite;
            c.a = 1f;
            if (m_materielLueur.HasProperty("_BaseColor")) m_materielLueur.SetColor("_BaseColor", c);
            if (m_materielLueur.HasProperty("_Color")) m_materielLueur.SetColor("_Color", c);
            // Les particules legacy doublent la couleur : on divise par 2 pour qu'intensité 1 = image telle quelle.
            if (m_materielLueur.HasProperty("_TintColor")) m_materielLueur.SetColor("_TintColor", c * 0.5f);
        }
    }

    private IEnumerator SequenceDeFin()
    {
        if (m_quadFlash == null) Debug.LogWarning("FinDuJeu : pas de quad de flash, la fin se fera sans blanc.");

        // --- 1. --- Laisser l'effet du sort démarrer
        if (m_attenteAvantBlanc > 0f) yield return new WaitForSeconds(m_attenteAvantBlanc);

        // --- 2. --- Montée au blanc
        yield return Fondu(0f, 1f, m_dureeMonteeBlanc);

        // --- 3. --- Écran entièrement blanc : on change tout sans que le joueur le voie
        PasserEnVirtuel();
        if (m_dureePauseBlanc > 0f) yield return new WaitForSeconds(m_dureePauseBlanc);

        // --- 4. --- Le blanc s'estompe
        yield return Fondu(1f, 0f, m_dureeDescenteBlanc);
    }

    // Fait passer l'opacité du flash de "de" à "vers" en "duree" secondes.
    private IEnumerator Fondu(float de, float vers, float duree)
    {
        for (float t = 0f; t < duree; t += Time.deltaTime)
        {
            AppliquerFondu(Mathf.Lerp(de, vers, t / duree));
            yield return null;
        }
        AppliquerFondu(vers);
    }

    // Place le quad du flash juste devant les yeux et le règle pour passer devant tout. Appelée au Start.
    private void PreparerFlash()
    {
        if (m_quadFlash == null) return;

        // Distance : juste derrière le plan de coupe proche de la caméra (le plus près qu'elle puisse afficher).
        // Ainsi rien ne peut se glisser entre les yeux et le quad, ni le bureau, ni les mains.
        Camera camera = m_quadFlash.GetComponentInParent<Camera>();
        float distance = (camera != null ? camera.nearClipPlane : 0.1f) + 0.01f;

        // Un quad Unity se voit depuis son côté -Z : posé en +Z devant la caméra, sans rotation, il lui fait face.
        // Taille = 6 × la distance : il déborde largement du champ de vision du casque.
        Transform t = m_quadFlash.transform;
        t.localPosition = new Vector3(0f, 0f, distance);
        t.localRotation = Quaternion.identity;
        t.localScale = new Vector3(distance * 6f, distance * 6f, 1f);

        m_quadFlash.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m_quadFlash.receiveShadows = false;

        // Copie du matériau, dessinée en tout dernier (file 5000, après tous les objets transparents).
        m_materielFlash = m_quadFlash.material;
        m_materielFlash.renderQueue = 5000;
        // Si le shader le permet, on ignore aussi la profondeur : le quad gagne toujours, même contre un objet plus proche.
        if (m_materielFlash.HasProperty("_ZTest"))
            m_materielFlash.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);

        m_quadFlash.gameObject.SetActive(false);   // invisible pendant le jeu
    }

    private void AppliquerFondu(float opacite)
    {
        if (m_materielFlash == null) return;

        // Opacité nulle : on désactive le quad, pour qu'il ne coûte rien et ne gêne rien.
        m_quadFlash.gameObject.SetActive(opacite > 0f);

        // material.color vise la couleur principale du shader : _BaseColor en URP, _Color sinon.
        Color c = m_couleurFlash;
        c.a = opacite;
        m_materielFlash.color = c;
    }

    private void PasserEnVirtuel()
    {
        // a) Arrêter le locomoteur AVANT de bouger le rig. Sinon sa capsule (restée là où le joueur
        //    s'était téléporté) ramènerait le rig vers elle à la fin de la frame.
        //    Ça coupe aussi tout déplacement : le joueur reste au bureau.
        if (m_locomoteur == null) m_locomoteur = FindFirstObjectByType<FirstPersonLocomotor>();
        if (m_locomoteur != null) m_locomoteur.enabled = false;

        // b) Ramener le rig à la pose du calibrage, puis arrêter TableCalibration :
        //    plus de recalibrage (geste Menu, recentrage) qui cacherait le décor, plus de recalage après téléportation.
        if (m_calibration != null)
        {
            m_calibration.RamenerAuBureau();
            m_calibration.enabled = false;
        }

        // c) Passthrough, fenêtre, téléportation : tout disparaît.
        foreach (GameObject objet in m_objetsADesactiver)
            if (objet != null) objet.SetActive(false);

        // d) L'image du décompte sur la table
        PoserImage();
    }

    private void PoserImage()
    {
        Transform table = m_calibration != null ? m_calibration.TableAnchor : null;
        if (table == null) { Debug.LogWarning("FinDuJeu : pas de TableAnchor, l'image n'est pas posée."); return; }

        // Orientation : Euler(90, 0, 0) couche le quad face vers le haut. Le haut de l'image part alors
        // vers le +Z de TableAnchor (le fond du bureau), donc le bas de l'image est du côté du joueur.
        // On ne garde que la rotation de la table autour de Y, pour que l'image reste bien à plat.
        Vector3 avantTable = Vector3.ProjectOnPlane(table.forward, Vector3.up).normalized;
        Quaternion rotation = Quaternion.LookRotation(avantTable, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);

        // Taille : la largeur est réglée dans l'Inspector, la hauteur suit les proportions du PNG.
        float proportions = m_textureBatons != null ? (float)m_textureBatons.height / m_textureBatons.width : 1f;
        Vector3 taille = new Vector3(m_largeurImage, m_largeurImage * proportions, 1f);

        // Depuis le centre de la table, on recule vers le joueur (-avantTable = vers le bord côté joueur).
        // Puis les bâtons 2 mm au-dessus du bois, la lueur 1 mm au-dessus des bâtons.
        Vector3 position = table.position
                         - avantTable * m_decalageVersJoueur
                         + Vector3.up * m_hauteurAuDessusTable;
        PlacerQuad(m_quadBatons, m_textureBatons, position, rotation, taille);
        PlacerQuad(m_quadLueur, m_textureLueur, position + Vector3.up * 0.001f, rotation, taille);

        // .material crée une copie propre à ce quad : on peut changer sa couleur sans toucher au fichier du matériau.
        if (m_quadLueur != null) m_materielLueur = m_quadLueur.material;
    }

    private static void PlacerQuad(Renderer quad, Texture texture, Vector3 position, Quaternion rotation, Vector3 taille)
    {
        if (quad == null) return;
        // On le sort de son parent pour que la taille ne dépende pas de l'échelle d'un parent.
        quad.transform.SetParent(null, false);
        quad.transform.SetPositionAndRotation(position, rotation);
        quad.transform.localScale = taille;
        if (texture != null) quad.material.mainTexture = texture;
        quad.gameObject.SetActive(true);
    }
}
