using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Au lancement, le joueur délimite sa table en pinçant 3 de ses coins, dans n'importe quel ordre.
// On aligne ensuite l'environnement virtuel dessus, on l'accroche à une ancre spatiale
// et on ouvre la fenêtre passthrough.
// En jeu : le geste Menu de la main gauche relance le calibrage.
public class TableCalibration : MonoBehaviour
{
    [Header("Mains et tête")]
    [SerializeField] private Hand[] m_hands;            // main gauche et main droite
    [SerializeField] private Transform m_head;          // CenterEyeAnchor : sert à savoir quel bord est face au joueur

    [Header("Scène")]
    [SerializeField] private Transform m_environment;   // racine de l'environnement virtuel
    [SerializeField] private Transform m_tableAnchor;   // dessus de la table virtuelle (enfant de l'environnement)
    [SerializeField] private Transform m_window;        // le quad passthrough (enfant de TableAnchor)
    [SerializeField] private GameObject m_markerPrefab; // petite sphère posée sur chaque coin
    [SerializeField] private Text m_instructions;       // texte affiché au joueur

    [Header("Réglages")]
    [SerializeField] private float m_margin = 0.05f;    // marge autour de la table, en mètres

    [Header("Événements")]
    [SerializeField] private UnityEvent m_onCalibrated; // appelé quand la table est validée

    private readonly List<Vector3> m_points = new List<Vector3>();
    private readonly List<GameObject> m_markers = new List<GameObject>();
    private bool[] m_wasPinching;   // la main pinçait-elle à l'image précédente ?
    private bool m_done;
    private GameObject m_anchorObject; // l'objet qui porte l'ancre spatiale (parent de l'environnement)

    private static readonly string[] Steps =
    {
        "Pince un coin de ta table",
        "Pince un autre coin",
        "Pince un troisième coin",
        "Pince encore pour valider"
    };

    private void Start()
    {
        m_wasPinching = new bool[m_hands.Length];
        Restart();
    }

    // Public : peut aussi être appelée depuis un bouton ou un autre script
    public void Restart()
    {
        // On décroche l'environnement de l'ancre AVANT de la détruire,
        // sinon il serait détruit avec elle. "true" = il garde sa position dans le monde.
        m_environment.SetParent(null, true);
        if (m_anchorObject != null) Destroy(m_anchorObject);
        m_anchorObject = null;

        foreach (GameObject m in m_markers) Destroy(m);
        m_markers.Clear();
        m_points.Clear();
        m_done = false;
        m_environment.gameObject.SetActive(false);   // on cache le virtuel : le joueur voit sa vraie table
        m_instructions.gameObject.SetActive(true);
        m_instructions.text = Steps[0];
    }

    private void Update()
    {
        // Pincement main GAUCHE paume vers soi = geste "Menu" du Quest,
        // vu par le SDK comme le bouton Start d'une manette -> on recalibre directement.
        if (OVRInput.GetDown(OVRInput.Button.Start)) Restart();

        for (int i = 0; i < m_hands.Length; i++)
        {
            bool pinching = m_hands[i].GetIndexFingerIsPinching();
            // On ne réagit qu'au DÉBUT du pincement, sinon un pincement tenu compterait à chaque image
            if (!m_done && pinching && !m_wasPinching[i]) OnPinch(m_hands[i]);
            // On met à jour m_wasPinching MÊME en jeu : sinon, juste après un recalibrage,
            // le pincement encore tenu serait pris pour un nouveau coin.
            m_wasPinching[i] = pinching;
        }
    }

    private void OnPinch(Hand hand)
    {
        if (m_points.Count == 3) { Validate(); return; }

        // Le point pincé = au milieu entre le bout du pouce et le bout de l'index
        if (!hand.GetJointPose(HandJointId.HandThumbTip, out Pose thumb) ||
            !hand.GetJointPose(HandJointId.HandIndexTip, out Pose index)) return;
        Vector3 point = (thumb.position + index.position) / 2f;

        m_points.Add(point);
        if (m_markerPrefab != null) m_markers.Add(Instantiate(m_markerPrefab, point, Quaternion.identity));
        m_instructions.text = Steps[m_points.Count];
    }

    private void Validate()
    {
        float height = (m_points[0].y + m_points[1].y + m_points[2].y) / 3f;   // hauteur = moyenne des 3 coins

        // On calcule à plat (sans la hauteur)
        Vector3 p0 = Flat(m_points[0]), p1 = Flat(m_points[1]), p2 = Flat(m_points[2]);

        // --- 1. Le bord avant = les 2 coins les plus proches du joueur ---
        // On trie les 3 points selon leur distance à la tête (à plat).
        // Les 2 premiers forment le bord face au joueur, le 3e est un coin du fond.
        Vector3 head = Flat(m_head.position);
        Vector3[] pts = { p0, p1, p2 };
        System.Array.Sort(pts, (a, b) => Vector3.Distance(a, head).CompareTo(Vector3.Distance(b, head)));
        Vector3 frontA = pts[0], frontB = pts[1], back = pts[2];

        // --- 2. Les axes de la table ---
        Vector3 side = (frontB - frontA).normalized;             // le long du bord avant (largeur)
        Vector3 forward = Vector3.Cross(side, Vector3.up);        // perpendiculaire au bord avant (profondeur)
        if (Vector3.Dot(back - frontA, forward) < 0f) forward = -forward;   // il doit pointer vers le fond de la table

        // --- 3. Dimensions et centre ---
        float width = Vector3.Distance(frontA, frontB);
        // Profondeur = distance du coin du fond au bord avant, mesurée perpendiculairement.
        // Ça marche que le coin du fond soit au-dessus de frontA ou de frontB.
        float depth = Vector3.Dot(back - frontA, forward);
        Vector3 center = (frontA + frontB) / 2f + forward * (depth / 2f);   // milieu du bord avant, avancé de la moitié de la profondeur
        center.y = height;

        // --- 4. Aligner l'environnement ---
        // On tourne d'abord (ce qui déplace aussi TableAnchor), puis on déplace.
        Quaternion targetRot = Quaternion.LookRotation(forward, Vector3.up);
        m_environment.rotation = targetRot * Quaternion.Inverse(m_tableAnchor.rotation) * m_environment.rotation;
        m_environment.position += center - m_tableAnchor.position;

        // La fenêtre prend la taille de la table (le quad est couché : x = largeur, y = profondeur)
        m_window.localScale = new Vector3(width + 2f * m_margin, depth + 2f * m_margin, 1f);

        // --- 5. Ancre spatiale ---
        // On crée un objet pile au centre de la vraie table et on lui ajoute un OVRSpatialAnchor.
        // Le casque va alors suivre ce point dans la VRAIE pièce : si le tracking dérive
        // ou si le joueur recentre la vue, l'ancre (et donc tout l'environnement enfant) reste collée à la table.
        m_anchorObject = new GameObject("TableSpatialAnchor");
        m_anchorObject.transform.SetPositionAndRotation(center, targetRot);
        m_anchorObject.AddComponent<OVRSpatialAnchor>();
        m_environment.SetParent(m_anchorObject.transform, true);   // true = ne bouge pas au moment du rattachement

        foreach (GameObject m in m_markers) Destroy(m);
        m_markers.Clear();
        m_instructions.gameObject.SetActive(false);
        m_environment.gameObject.SetActive(true);
        m_done = true;
        m_onCalibrated.Invoke();
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}