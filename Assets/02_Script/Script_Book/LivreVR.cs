using System.Collections;
using TMPro;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Livre articulé : s'ouvre / se ferme et tourne ses pages en animant les os du modèle
/// SM_FantasyBook_MiddleOpen (Book_Open pour la couverture, Page_01/02/03 pour la feuille qui tourne).
///
/// Astuce utilisée pour les pages : une seule feuille 3D tourne. Pendant qu'elle tourne, son recto montre
/// la page qu'on quitte, son verso la page suivante, et la page d'en dessous est déjà écrite.
/// Une fois posée, la feuille revient instantanément à droite avec les nouveaux textes : on ne voit pas la différence.
/// </summary>
public class LivreVR : MonoBehaviour
{
    [Header("Contenu")]
    [Tooltip("Texte de chaque page, dans l'ordre. Livre ouvert : pages 1-2, puis 3-4, etc.")]
    [TextArea(2, 10)]
    public string[] pages =
    {
        "Page 1", "Page 2", "Page 3", "Page 4", "Page 5", "Page 6", "Page 7", "Page 8",
    };

    [Header("Animation")]
    public float dureeOuverture = 0.8f;
    public float dureeTourPage = 0.7f;
    [Tooltip("Courbure de la feuille pendant qu'elle tourne (en degrés). Si elle se plie dans le mauvais sens, mets une valeur négative.")]
    public float courbure = 30f;
    public bool ouvertAuDemarrage;

    [Header("Test au clavier (sans casque)")]
    [Tooltip("O : ouvrir / fermer. Flèche droite : page suivante. Flèche gauche : page précédente.")]
    public bool raccourcisClavier = true;

    [Header("Son (optionnel)")]
    public AudioSource sonPage;

    [Header("Références (remplies par Tools > Livre VR)")]
    public Transform osOuverture;
    public Transform osFeuille1;
    public Transform osFeuille2;
    public Transform osFeuille3;

    public TMP_Text texteGauche;
    public TMP_Text texteRecto;
    public TMP_Text texteVerso;
    public TMP_Text texteSousFeuille;

    public GameObject zoneCouverture;
    public GameObject zonePageSuivante;
    public GameObject zonePagePrecedente;

    public BoxCollider colliderLivre;

    [Header("Prise en main")]
    [Tooltip("L'objet ISDK_HandGrabInteraction du livre (enfant direct de Livre VR), en entier. Fermé = on peut le prendre, ouvert = il reste posé et seuls les pincements marchent.")]
    public GameObject grabLivre;
    [Tooltip("Coché : on peut aussi déplacer le livre ouvert. Le grab est coupé seulement pendant qu'une page tourne (sauf si le livre est déjà tenu).")]
    public bool grabQuandOuvert;
    public Vector3 colliderFermeCentre;
    public Vector3 colliderFermeTaille;
    public Vector3 colliderOuvertCentre;
    public Vector3 colliderOuvertTaille;

    Quaternion m_RepOuverture;
    Quaternion m_RepFeuille1;
    Quaternion m_RepFeuille2;
    Quaternion m_RepFeuille3;

    int m_IndexPageGauche;
    bool m_Ouvert;
    bool m_Occupe;

    // Manipulation à la main (voir la section plus bas).
    int m_SensManuel;       // +1 = vers la page suivante, -1 = vers la page précédente
    float m_AngleManuel;    // angle actuel de la feuille (0 = à droite, 180 = à gauche)

    [Header("Tour de page à la main")]
    [Tooltip("Plus c'est grand, plus la feuille colle vite à la main (moins de lissage).")]
    public float reactiviteMain = 20f;
    [Tooltip("Coche si la couverture se referme quand tu tires pour l'ouvrir (et inversement).")]
    public bool inverserSensCouverture;

    public bool EstOuvert => m_Ouvert;

    // --- Pour les scripts qui affichent du contenu sur les pages (ex : SymbolesLivre) ----------
    // Le livre a 4 "surfaces" (voir l'astuce en haut). On retient quelle page chacune affiche.
    public enum Surface { Gauche, Recto, Verso, SousFeuille }
    readonly int[] m_PageSur = new int[4];

    public bool EstOccupe => m_Occupe;
    public int PageSur(Surface s) => m_PageSur[(int)s];

    public TMP_Text TexteDe(Surface s)
    {
        switch (s)
        {
            case Surface.Gauche: return texteGauche;
            case Surface.Recto: return texteRecto;
            case Surface.Verso: return texteVerso;
            default: return texteSousFeuille;
        }
    }

    // Écrit une page sur une surface ET retient son numéro.
    void Ecrire(Surface s, int index)
    {
        m_PageSur[(int)s] = index;
        TexteDe(s).text = Page(index);
    }

    /// <summary>
    /// Change le contenu d'une page pendant le jeu (ex : nouvelle image quand une énigme est résolue).
    /// Si la page est affichée en ce moment, elle est mise à jour tout de suite.
    /// </summary>
    public void RemplacerPage(int index, string contenu)
    {
        if (index < 0 || index >= pages.Length)
            return;

        pages[index] = contenu;
        for (var s = 0; s < 4; s++)
        {
            if (m_PageSur[s] == index)
                TexteDe((Surface)s).text = contenu;
        }
    }

    void Awake()
    {
        m_RepOuverture = osOuverture.localRotation;
        m_RepFeuille1 = osFeuille1.localRotation;
        m_RepFeuille2 = osFeuille2.localRotation;
        m_RepFeuille3 = osFeuille3.localRotation;

        // Axe de courbure : pour que la feuille se plie, ses os doivent tourner autour d'un axe
        // PARALLÈLE à la reliure (le même que celui qui fait tourner la feuille), exprimé dans le
        // repère de chaque os. Avant, on utilisait Vector3.right, qui ne correspond pas forcément.
        var axeReliureMonde = osFeuille1.parent.TransformDirection(Vector3.up);
        m_AxePli2 = osFeuille2.parent.InverseTransformDirection(axeReliureMonde).normalized;
        m_AxePli3 = osFeuille3.parent.InverseTransformDirection(axeReliureMonde).normalized;
    }

    Vector3 m_AxePli2 = Vector3.right;
    Vector3 m_AxePli3 = Vector3.right;

    void Start()
    {
        m_Ouvert = ouvertAuDemarrage;
        PoserOuverture(m_Ouvert ? 180f : 0f);
        PoserFeuille(0f, 0f);
        AfficherDoublePage();
        MettreAJourEtat();
    }

    void Update()
    {
        var clavier = Keyboard.current;
        if (!raccourcisClavier || clavier == null)
            return;

        if (clavier.oKey.wasPressedThisFrame)
            OuvrirOuFermer();
        else if (clavier.rightArrowKey.wasPressedThisFrame)
            PageSuivante();
        else if (clavier.leftArrowKey.wasPressedThisFrame)
            PagePrecedente();
    }

    // --- Actions publiques -------------------------------------------------------------------

    [ContextMenu("Ouvrir / Fermer")]
    public void OuvrirOuFermer()
    {
        if (m_Occupe)
            return;

        StartCoroutine(m_Ouvert ? Fermer() : Ouvrir());
    }

    // Ouvre seulement (ne referme jamais). À brancher sur le "When Select" du grab du livre.
    public void OuvrirSiFerme()
    {
        if (!m_Occupe && !m_Ouvert)
            StartCoroutine(Ouvrir());
    }

    // Ferme seulement. Optionnel : à brancher sur le "When Unselect" du grab du livre.
    public void FermerSiOuvert()
    {
        if (!m_Occupe && m_Ouvert)
            StartCoroutine(Fermer());
    }

    [ContextMenu("Page suivante")]
    public void PageSuivante()
    {
        if (m_Occupe)
            return;

        if (!m_Ouvert)
            StartCoroutine(Ouvrir());
        else if (m_IndexPageGauche + 2 < pages.Length)
            StartCoroutine(TournerVersLaDroite());
    }

    [ContextMenu("Page précédente")]
    public void PagePrecedente()
    {
        if (m_Occupe || !m_Ouvert)
            return;

        // Sur la première double page, revenir en arrière referme le livre.
        StartCoroutine(m_IndexPageGauche >= 2 ? TournerVersLaGauche() : Fermer());
    }

    // --- Ouverture / fermeture ---------------------------------------------------------------

    IEnumerator Ouvrir()
    {
        m_Occupe = true;
        m_Ouvert = true;
        AfficherTextes(true);
        MettreAJourEtat();
        yield return Animer(dureeOuverture, t => PoserOuverture(Mathf.Lerp(0f, 180f, t)));
        m_Occupe = false;
    }

    IEnumerator Fermer()
    {
        m_Occupe = true;
        yield return Animer(dureeOuverture, t => PoserOuverture(Mathf.Lerp(180f, 0f, t)));
        m_Ouvert = false;
        MettreAJourEtat();
        m_Occupe = false;
    }

    // --- Pages -------------------------------------------------------------------------------

    IEnumerator TournerVersLaDroite()
    {
        m_Occupe = true;
        JouerSon();

        var i = m_IndexPageGauche;
        Ecrire(Surface.Verso, i + 2);
        Ecrire(Surface.SousFeuille, i + 3);
        texteVerso.enabled = true;
        texteSousFeuille.enabled = true;

        yield return Animer(dureeTourPage, t => PoserFeuille(Mathf.Lerp(0f, 180f, t), courbure * Mathf.Sin(t * Mathf.PI)));

        m_IndexPageGauche += 2;
        PoserFeuille(0f, 0f);
        AfficherDoublePage();
        m_Occupe = false;
    }

    IEnumerator TournerVersLaGauche()
    {
        m_Occupe = true;
        JouerSon();

        // La feuille saute à gauche en montrant la page actuelle, puis revient à droite.
        var i = m_IndexPageGauche;
        Ecrire(Surface.Verso, i);
        Ecrire(Surface.Gauche, i - 2);
        Ecrire(Surface.Recto, i - 1);
        Ecrire(Surface.SousFeuille, i + 1);
        texteVerso.enabled = true;
        texteSousFeuille.enabled = true;
        PoserFeuille(180f, 0f);

        yield return Animer(dureeTourPage, t => PoserFeuille(Mathf.Lerp(180f, 0f, t), -courbure * Mathf.Sin(t * Mathf.PI)));

        m_IndexPageGauche -= 2;
        PoserFeuille(0f, 0f);
        AfficherDoublePage();
        m_Occupe = false;
    }

    // --- Manipulation à la main (feuille ou couverture) -------------------------------------
    // Appelé par ZoneTournerPage : Commencer... au pincement, SuivreTourManuel à chaque mouvement,
    // TerminerTourManuel au lâcher. Marche pareil pour une feuille et pour la couverture.

    enum ModeManuel { Aucun, Feuille, Couverture }
    ModeManuel m_Mode = ModeManuel.Aucun;
    float m_CibleCouverture;      // angle visé par la main pour la couverture (avant lissage)
    float m_AngleMainPrecedent;   // pour mesurer de combien la main a tourné depuis l'image précédente
    bool m_AngleMainInitialise;

    /// <summary>Pincement sur la couverture fermée : on commence à l'ouvrir à la main.</summary>
    public bool CommencerOuvertureManuelle()
    {
        if (m_Occupe || m_Ouvert)
            return false;

        return DemarrerCouverture();
    }

    /// <summary>Pincement sur une page. Renvoie false si on ne peut pas tourner maintenant.</summary>
    public bool CommencerTourManuel(bool versPageSuivante)
    {
        if (m_Occupe || !m_Ouvert)
            return false;

        var i = m_IndexPageGauche;

        if (versPageSuivante)
        {
            if (i + 2 >= pages.Length)
                return false; // dernière double page

            // Même préparation que TournerVersLaDroite.
            Ecrire(Surface.Verso, i + 2);
            Ecrire(Surface.SousFeuille, i + 3);
            m_AngleManuel = 0f;
        }
        else
        {
            // Première double page : tirer la page de gauche referme la couverture à la main.
            if (i < 2)
                return DemarrerCouverture();

            // Même préparation que TournerVersLaGauche : la feuille saute à gauche.
            Ecrire(Surface.Verso, i);
            Ecrire(Surface.Gauche, i - 2);
            Ecrire(Surface.Recto, i - 1);
            Ecrire(Surface.SousFeuille, i + 1);
            m_AngleManuel = 180f;
        }

        texteVerso.enabled = true;
        texteSousFeuille.enabled = true;

        m_Occupe = true;
        m_Mode = ModeManuel.Feuille;
        PauseGrabPendantManip();
        m_SensManuel = versPageSuivante ? 1 : -1;
        PoserFeuilleManuelle(m_AngleManuel);
        JouerSon();
        return true;
    }

    bool DemarrerCouverture()
    {
        m_Occupe = true;
        m_Mode = ModeManuel.Couverture;
        PauseGrabPendantManip();
        m_SensManuel = m_Ouvert ? -1 : 1; // +1 = on ouvre, -1 = on ferme
        m_AngleManuel = m_Ouvert ? 180f : 0f;
        m_CibleCouverture = m_AngleManuel;
        m_AngleMainInitialise = false;

        if (!m_Ouvert)
            AfficherTextes(true); // les pages apparaissent pendant qu'on soulève la couverture
        return true;
    }

    /// <summary>Fait suivre la feuille ou la couverture à la position de la main (coordonnées monde).</summary>
    public void SuivreTourManuel(Vector3 positionMain)
    {
        // Lissage : on se rapproche de l'angle visé sans à-coups, même si le suivi de main tremble.
        var lissage = 1f - Mathf.Exp(-reactiviteMain * Time.deltaTime);

        if (m_Mode == ModeManuel.Feuille)
        {
            var cible = AngleDepuisPosition(positionMain);
            PoserFeuilleManuelle(Mathf.Lerp(m_AngleManuel, cible, lissage));
        }
        else if (m_Mode == ModeManuel.Couverture)
        {
            // Pour la couverture, on suit de combien la main a TOURNÉ autour de la reliure
            // depuis l'image précédente, et on ajoute ce mouvement à l'angle de la couverture.
            var angleMain = AngleAutourDe(osOuverture, positionMain);
            if (!m_AngleMainInitialise)
            {
                m_AngleMainPrecedent = angleMain;
                m_AngleMainInitialise = true;
                return;
            }

            var delta = Mathf.DeltaAngle(m_AngleMainPrecedent, angleMain);
            if (inverserSensCouverture)
                delta = -delta;
            m_CibleCouverture = Mathf.Clamp(m_CibleCouverture + delta, 0f, 180f);
            m_AngleMainPrecedent = angleMain;

            m_AngleManuel = Mathf.Lerp(m_AngleManuel, m_CibleCouverture, lissage);
            PoserOuverture(m_AngleManuel);
        }
    }

    /// <summary>Au lâcher : ça finit le mouvement si on a passé la moitié, sinon ça revient.</summary>
    public void TerminerTourManuel(bool forcerTourComplet)
    {
        if (m_Mode == ModeManuel.Aucun)
            return;

        var passeLaMoitie = m_SensManuel > 0 ? m_AngleManuel > 90f : m_AngleManuel < 90f;
        StartCoroutine(FinirManuel(forcerTourComplet || passeLaMoitie));
    }

    IEnumerator FinirManuel(bool complet)
    {
        var depart = m_AngleManuel;
        // Sens +1 (page suivante / ouvrir) : complet = 180. Sens -1 (page précédente / fermer) : complet = 0.
        var cible = (m_SensManuel > 0) == complet ? 180f : 0f;

        if (m_Mode == ModeManuel.Feuille)
        {
            var duree = Mathf.Max(0.1f, dureeTourPage * Mathf.Abs(cible - depart) / 180f);
            yield return Animer(duree, t => PoserFeuilleManuelle(Mathf.Lerp(depart, cible, t)));

            if (complet)
                m_IndexPageGauche += 2 * m_SensManuel;

            // Comme pour l'animation auto : la feuille revient à droite avec les bons textes.
            PoserFeuille(0f, 0f);
            AfficherDoublePage();
        }
        else
        {
            var duree = Mathf.Max(0.1f, dureeOuverture * Mathf.Abs(cible - depart) / 180f);
            yield return Animer(duree, t => PoserOuverture(Mathf.Lerp(depart, cible, t)));

            m_Ouvert = cible >= 180f;
            MettreAJourEtat(); // textes, zones actives et taille du collider
        }

        m_Mode = ModeManuel.Aucun;
        m_Occupe = false;
        MettreAJourEtat(); // remet le grab dans l'état normal
    }

    // Pendant qu'on tourne une page, la main qui pince ne doit pas aussi attraper le livre.
    // Si le livre est déjà tenu (par l'autre main), on ne coupe rien, sinon il tomberait.
    void PauseGrabPendantManip()
    {
        if (grabLivre == null || !grabLivre.activeSelf)
            return;

        var grab = grabLivre.GetComponent<HandGrabInteractable>();
        if (grab != null && grab.State == InteractableState.Select)
            return;

        grabLivre.SetActive(false);
    }

    void PoserFeuilleManuelle(float angle)
    {
        m_AngleManuel = angle;
        // Courbure maximale quand la feuille est verticale (90°), nulle quand elle est posée.
        PoserFeuille(angle, m_SensManuel * courbure * Mathf.Sin(angle * Mathf.Deg2Rad));
    }

    /// <summary>
    /// Convertit la position de la main en angle de feuille (0 à 180).
    /// On travaille dans le repère du parent de la feuille, où elle tourne autour de l'axe "up".
    /// "Droite" = direction opposée à la page de gauche (texteGauche), vue depuis la reliure.
    /// </summary>
    float AngleDepuisPosition(Vector3 positionMain)
    {
        var parent = osFeuille1.parent;
        var pivot = osFeuille1.localPosition;
        var axe = Vector3.up;

        var versMain = Vector3.ProjectOnPlane(parent.InverseTransformPoint(positionMain) - pivot, axe);
        var versGauche = Vector3.ProjectOnPlane(parent.InverseTransformPoint(texteGauche.transform.position) - pivot, axe);

        if (versMain.sqrMagnitude < 1e-6f || versGauche.sqrMagnitude < 1e-6f)
            return m_AngleManuel;

        var angle = Vector3.SignedAngle(-versGauche, versMain, axe);

        // Main sous le livre : on la colle au côté le plus proche.
        if (angle < 0f)
            angle = angle < -90f ? 180f : 0f;

        return Mathf.Clamp(angle, 0f, 180f);
    }

    /// <summary>Angle de la main autour de l'axe de rotation d'un os (repère de son parent, axe "up").</summary>
    static float AngleAutourDe(Transform os, Vector3 positionMain)
    {
        var versMain = Vector3.ProjectOnPlane(os.parent.InverseTransformPoint(positionMain) - os.localPosition, Vector3.up);
        return Vector3.SignedAngle(Vector3.forward, versMain, Vector3.up);
    }

    // --- Outils ------------------------------------------------------------------------------

    void AfficherDoublePage()
    {
        Ecrire(Surface.Gauche, m_IndexPageGauche);
        Ecrire(Surface.Recto, m_IndexPageGauche + 1);
        texteVerso.enabled = false;
        texteSousFeuille.enabled = false;
    }

    void AfficherTextes(bool visibles)
    {
        texteGauche.enabled = visibles;
        texteRecto.enabled = visibles;
        if (!visibles)
        {
            texteVerso.enabled = false;
            texteSousFeuille.enabled = false;
        }
    }

    void MettreAJourEtat()
    {
        AfficherTextes(m_Ouvert);

        if (zoneCouverture != null)
            zoneCouverture.SetActive(!m_Ouvert);
        if (zonePageSuivante != null)
            zonePageSuivante.SetActive(m_Ouvert);
        if (zonePagePrecedente != null)
            zonePagePrecedente.SetActive(m_Ouvert);

        // Ouvert : plus de grab, donc aucun conflit avec les pincements sur les pages.
        // Si le livre était tenu à ce moment-là, le désactiver le lâche simplement.
        // On désactive l'objet ENTIER : tous ses interactables (Hand Grab, Grab manettes...) sont coupés d'un coup.
        if (grabLivre != null)
            grabLivre.SetActive(!m_Ouvert || grabQuandOuvert);

        if (colliderLivre != null)
        {
            colliderLivre.center = m_Ouvert ? colliderOuvertCentre : colliderFermeCentre;
            colliderLivre.size = m_Ouvert ? colliderOuvertTaille : colliderFermeTaille;
        }
    }

    /// <param name="angle">0 = fermé, 180 = ouvert à plat.</param>
    void PoserOuverture(float angle)
    {
        osOuverture.localRotation = Quaternion.AngleAxis(angle, Vector3.up) * m_RepOuverture;
    }

    /// <param name="angle">0 = feuille posée à droite, 180 = feuille posée à gauche.</param>
    void PoserFeuille(float angle, float pli)
    {
        osFeuille1.localRotation = Quaternion.AngleAxis(angle, Vector3.up) * m_RepFeuille1;
        osFeuille2.localRotation = Quaternion.AngleAxis(pli, m_AxePli2) * m_RepFeuille2;
        osFeuille3.localRotation = Quaternion.AngleAxis(pli * 0.8f, m_AxePli3) * m_RepFeuille3;
    }

    static IEnumerator Animer(float duree, System.Action<float> appliquer)
    {
        for (var temps = 0f; temps < duree; temps += Time.deltaTime)
        {
            appliquer(Mathf.SmoothStep(0f, 1f, temps / duree));
            yield return null;
        }

        appliquer(1f);
    }

    void JouerSon()
    {
        if (sonPage != null)
            sonPage.Play();
    }

    string Page(int index)
    {
        return index >= 0 && index < pages.Length ? pages[index] : string.Empty;
    }
}