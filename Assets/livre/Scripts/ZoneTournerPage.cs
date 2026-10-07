using Oculus.Interaction.Input;
using UnityEngine;

/// <summary>
/// Zone du livre à pincer : couverture, page suivante ou page précédente.
///
/// Le pincement est détecté directement à partir des mains du Meta SDK (bout du pouce + bout de l'index),
/// sans Hand Grab Interactable sur la zone.
///
/// Règle du livre (gérée par LivreVR) : fermé = on peut le prendre, ouvert = il reste posé.
/// - Couverture : un pincement ouvre le livre automatiquement (le grab se coupe tout seul à l'ouverture).
/// - Pages : pincer, bouger la main = la feuille suit, lâcher = elle finit ou revient.
///   Un pincement très court tourne la page d'un coup.
///
/// La zone n'a besoin que d'un Collider (Is Trigger conseillé) et de ce script.
/// </summary>
public class ZoneTournerPage : MonoBehaviour
{
    // Garde le même nom que l'ancienne version : l'outil Editor (LivreSetup) l'utilise.
    public enum Action
    {
        PageSuivante,
        PagePrecedente,
        OuvrirFermer, // couverture : un pincement ouvre le livre
    }

    public LivreVR livre;
    public Action action = Action.PageSuivante;

    [Tooltip("Collider de la zone. Laisse vide : celui de cet objet.")]
    public Collider zone;
    [Tooltip("Tolérance autour de la zone pour accepter le pincement (en mètres).")]
    public float marge = 0.02f;

    [Header("Pincement court = tour automatique")]
    public float dureePincementCourt = 0.25f;
    public float distancePincementCourt = 0.03f; // en mètres

    [Header("Debug")]
    public bool afficherLogs = true;

    // Les mains sont partagées par toutes les zones.
    static Hand[] s_Mains;
    static float s_ProchaineRecherche;

    bool[] m_PincaitAvant = new bool[0];
    Hand m_MainActive;          // la main qui tient la feuille (null = personne)
    float m_TempsDebut;
    Vector3 m_PositionDebut;
    Vector3 m_DernierePosition;

    void Awake()
    {
        if (zone == null)
            zone = GetComponent<Collider>();
        if (livre == null)
            livre = GetComponentInParent<LivreVR>();
    }

    void OnEnable()
    {
        // La zone vient d'apparaître : un pincement déjà en cours ne compte pas.
        for (var i = 0; i < m_PincaitAvant.Length; i++)
            m_PincaitAvant[i] = true;
    }

    void OnDisable()
    {
        if (m_MainActive != null)
            Relacher();
    }

    void Update()
    {
        if (livre == null || zone == null || !TrouverMains())
            return;

        if (m_PincaitAvant.Length != s_Mains.Length)
            m_PincaitAvant = new bool[s_Mains.Length];

        for (var i = 0; i < s_Mains.Length; i++)
        {
            var main = s_Mains[i];
            if (main == null || !main.IsTrackedDataValid || !PointDePincement(main, out var point))
            {
                // Main perdue par le casque : si c'est elle qui tenait la feuille, on lâche.
                if (m_MainActive != null && main == m_MainActive)
                    Relacher();
                m_PincaitAvant[i] = false;
                continue;
            }

            var pince = main.GetFingerIsPinching(HandFinger.Index);

            if (m_MainActive == null)
            {
                // Début de pincement (pas pincé avant, pincé maintenant) DANS la zone.
                if (pince && !m_PincaitAvant[i] && DansLaZone(point))
                    Commencer(main, point);
            }
            else if (main == m_MainActive)
            {
                if (pince)
                {
                    m_DernierePosition = point;
                    livre.SuivreTourManuel(point);
                }
                else
                {
                    Relacher();
                }
            }

            m_PincaitAvant[i] = pince;
        }
    }

    void Commencer(Hand main, Vector3 point)
    {
        // Couverture : ouverture automatique, rien à suivre ensuite.
        if (action == Action.OuvrirFermer)
        {
            if (afficherLogs)
                Debug.Log($"[ZoneTournerPage] {name} : pincement sur la couverture, ouverture");
            livre.OuvrirSiFerme();
            return;
        }

        var commence = livre.CommencerTourManuel(action == Action.PageSuivante);

        if (afficherLogs)
            Debug.Log($"[ZoneTournerPage] {name} : pincement {(commence ? "accepté" : "refusé (occupé ou dernière page)")}");

        if (!commence)
            return;

        m_MainActive = main;
        m_TempsDebut = Time.time;
        m_PositionDebut = m_DernierePosition = point;
    }

    void Relacher()
    {
        var pincementCourt = Time.time - m_TempsDebut < dureePincementCourt
                             && Vector3.Distance(m_PositionDebut, m_DernierePosition) < distancePincementCourt;

        livre.TerminerTourManuel(pincementCourt);
        m_MainActive = null;
    }

    bool DansLaZone(Vector3 point)
    {
        if (!zone.enabled)
            return false;
        // ClosestPoint renvoie le point lui-même s'il est dans le collider.
        return Vector3.Distance(zone.ClosestPoint(point), point) <= marge;
    }

    // Point de pincement = milieu entre le bout du pouce et le bout de l'index.
    static bool PointDePincement(Hand main, out Vector3 point)
    {
        point = default;
        if (!main.GetJointPose(HandJointId.HandIndexTip, out var index) ||
            !main.GetJointPose(HandJointId.HandThumbTip, out var pouce))
            return false;

        point = (index.position + pouce.position) * 0.5f;
        return true;
    }

    static bool TrouverMains()
    {
        if (s_Mains != null && s_Mains.Length > 0)
            return true;
        if (Time.time < s_ProchaineRecherche)
            return false;

        s_ProchaineRecherche = Time.time + 1f; // on réessaie au plus une fois par seconde
        s_Mains = FindObjectsByType<Hand>(FindObjectsSortMode.None);
        Debug.Log($"[ZoneTournerPage] Mains trouvées : {s_Mains.Length}");
        return s_Mains.Length > 0;
    }
}