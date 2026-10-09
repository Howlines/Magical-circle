using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Menu de départ (Start / Options / Quitter), utilisable avec les mains sur Quest 3 :
/// toucher du doigt (poke) ou rayon + pincement, grâce aux interactions Meta ajoutées sur le canvas.
/// Play cache le menu et affiche un message mystérieux. Quand le joueur ferme le message,
/// les objets de la liste "Activer Apres Start" sont activés (ex : la calibration de la table).
/// </summary>
public class MenuDepart : MonoBehaviour
{
    [Header("Panneaux")]
    public GameObject panneauPrincipal;
    public GameObject panneauOptions;

    [Header("Options")]
    public Slider sliderVolume;
    public TMP_Text texteVolume;

    [Header("Message après Start")]
    public MessageMystere message;
    public float dureeFonduMessage = 2f;

    [Header("Lancé après le message")]
    [Tooltip("Objets désactivés au départ, activés quand le joueur ferme le message (ex : TableCalibration).")]
    public GameObject[] activerApresStart;

    [Header("Placement devant le joueur")]
    [Tooltip("Tête du joueur (CenterEyeAnchor). Vide = caméra principale.")]
    public Transform tete;
    [Tooltip("Distance du menu : à portée de main pour pouvoir le toucher du doigt.")]
    public float distance = 0.55f;
    [Tooltip("Le menu est un peu plus bas que les yeux, plus confortable pour les bras.")]
    public float sousLesYeux = 0.2f;
    public float distanceMessage = 1.2f;

    [Header("Test au clavier (sans casque)")]
    [Tooltip("Entrée : Start. Tab : Options. Retour arrière : Retour. Échap : Quitter.")]
    public bool raccourcisClavier = true;

    [Header("Animation")]
    public float dureeApparition = 1.2f;

    const string k_CleVolume = "MagicalCircle.Volume";
    bool m_EnTrainDePartir;

    void Awake()
    {
        AudioListener.volume = PlayerPrefs.GetFloat(k_CleVolume, 1f);
        if (sliderVolume != null)
        {
            sliderVolume.SetValueWithoutNotify(AudioListener.volume);
            sliderVolume.onValueChanged.AddListener(ChangerVolume);
        }
        AfficherVolume();

        if (message != null)
            message.gameObject.SetActive(false);
        AfficherPanneau(principal: true);
    }

    IEnumerator Start()
    {
        var groupe = GetComponent<CanvasGroup>();
        if (groupe != null)
            groupe.alpha = 0f;

        // On attend que le casque ait donné la position de la tête avant de placer le menu.
        yield return null;
        yield return null;
        PlacerDevant(transform, distance, sousLesYeux);
        yield return Apparition(0f, 1f, dureeApparition);
    }

    IEnumerator Apparition(float de, float vers, float duree)
    {
        var groupe = GetComponent<CanvasGroup>();
        var echelle = transform.localScale;
        for (var t = 0f; t < duree; t += Time.unscaledDeltaTime)
        {
            var k = Mathf.SmoothStep(0f, 1f, t / duree);
            var a = Mathf.Lerp(de, vers, k);
            if (groupe != null)
                groupe.alpha = a;
            transform.localScale = echelle * Mathf.Lerp(0.92f, 1f, a);
            yield return null;
        }
        if (groupe != null)
            groupe.alpha = vers;
        transform.localScale = echelle;
    }

    IEnumerator Partir()
    {
        m_EnTrainDePartir = true;
        Debug.Log($"[DBG] MenuDepart.Partir : message = {(message != null ? message.name : "NULL")}"); // [DBG] temporaire

        // Le message lancera la suite quand le joueur l'aura fermé.
        // Sans message, on lance la suite tout de suite.
        if (message != null)
            message.Afficher(this, LancerLaSuite);
        else
            LancerLaSuite();

        yield return Apparition(1f, 0f, dureeApparition * 0.6f);
        gameObject.SetActive(false);
    }

    /// <summary>Active les objets de la liste (calibration, etc.).</summary>
    public void LancerLaSuite()
    {
        if (activerApresStart == null)
            return;
        foreach (var objet in activerApresStart)
            if (objet != null)
                objet.SetActive(true);
    }

    void Update()
    {
        var clavier = Keyboard.current;
        if (!raccourcisClavier || clavier == null)
            return;

        if (clavier.enterKey.wasPressedThisFrame || clavier.numpadEnterKey.wasPressedThisFrame)
            Commencer();
        else if (clavier.tabKey.wasPressedThisFrame)
            OuvrirOptions();
        else if (clavier.backspaceKey.wasPressedThisFrame)
            RetourMenu();
        else if (clavier.escapeKey.wasPressedThisFrame)
            Quitter();
    }

    // --- Boutons -----------------------------------------------------------------------------

    public void Commencer()
    {
        Debug.Log($"[DBG] MenuDepart.Commencer (déjà en train de partir : {m_EnTrainDePartir})"); // [DBG] temporaire
        if (!m_EnTrainDePartir)
            StartCoroutine(Partir());
    }

    public void OuvrirOptions() => AfficherPanneau(principal: false);

    public void RetourMenu() => AfficherPanneau(principal: true);

    public void Quitter()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // --- Outils ------------------------------------------------------------------------------

    void ChangerVolume(float valeur)
    {
        AudioListener.volume = valeur;
        PlayerPrefs.SetFloat(k_CleVolume, valeur);
        AfficherVolume();
    }

    void AfficherVolume()
    {
        if (texteVolume != null)
            texteVolume.text = $"Volume : {Mathf.RoundToInt(AudioListener.volume * 100f)} %";
    }

    void AfficherPanneau(bool principal)
    {
        if (panneauPrincipal != null)
            panneauPrincipal.SetActive(principal);
        if (panneauOptions != null)
            panneauOptions.SetActive(!principal);
    }

    /// <summary>Place un objet face au joueur, à l'horizontale de son regard.</summary>
    public void PlacerDevant(Transform objet, float dist, float plusBas)
    {
        var t = tete != null ? tete : Camera.main != null ? Camera.main.transform : null;
        Debug.Log($"[DBG] PlacerDevant({objet.name}) : tête = {(t != null ? t.name + " " + t.position : "NULL → pas de placement")}"); // [DBG] temporaire
        if (t == null)
            return;

        var avant = Vector3.ProjectOnPlane(t.forward, Vector3.up);
        if (avant.sqrMagnitude < 0.001f)
            avant = Vector3.ProjectOnPlane(t.up, Vector3.up);
        avant.Normalize();

        objet.position = t.position + avant * dist + Vector3.down * plusBas;
        // Un canvas se lit depuis son côté -Z : son "avant" doit regarder à l'opposé du joueur.
        objet.rotation = Quaternion.LookRotation(avant, Vector3.up);
    }
}