using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Au lancement, le joueur délimite sa table en pinçant 3 de ses coins, dans n'importe quel ordre.
// On aligne ensuite l'environnement virtuel dessus et on ouvre la fenêtre passthrough.
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

    // Public : pourra être appelée plus tard pour refaire le calibrage
    public void Restart()
    {
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
        if (m_done) return;

        for (int i = 0; i < m_hands.Length; i++)
        {
            bool pinching = m_hands[i].GetIndexFingerIsPinching();
            // On ne réagit qu'au DÉBUT du pincement, sinon un pincement tenu compterait à chaque image
            if (pinching && !m_wasPinching[i]) OnPinch(m_hands[i]);
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

        // --- 1. Trouver la diagonale ---
        // Sur 3 coins d'un rectangle, les deux points les plus éloignés sont forcément sur la diagonale.
        // Le 3e point est le coin "entre les deux" (celui qui touche les deux bords).
        Vector3 corner, q, r;
        float d01 = Vector3.Distance(p0, p1), d02 = Vector3.Distance(p0, p2), d12 = Vector3.Distance(p1, p2);
        if (d12 >= d01 && d12 >= d02)      { corner = p0; q = p1; r = p2; }
        else if (d02 >= d01 && d02 >= d12) { corner = p1; q = p0; r = p2; }
        else                               { corner = p2; q = p0; r = p1; }

        // --- 2. Les deux axes de la table ---
        Vector3 axis1 = (q - corner).normalized;             // le long d'un bord
        Vector3 axis2 = Vector3.Cross(Vector3.up, axis1);     // l'autre bord, bien perpendiculaire
        Vector3 diagonal = r - q;
        float size1 = Mathf.Abs(Vector3.Dot(diagonal, axis1));   // longueur du bord le long de axis1
        float size2 = Mathf.Abs(Vector3.Dot(diagonal, axis2));   // longueur du bord le long de axis2
        Vector3 center = (q + r) / 2f;                        // le centre = milieu de la diagonale
        center.y = height;

        // --- 3. Quel bord est face au joueur ? ---
        // "forward" doit pointer du joueur vers le fond de la table.
        Vector3 toTable = Flat(center - m_head.position);
        Vector3 forward;
        float width, depth;
        if (Mathf.Abs(Vector3.Dot(toTable, axis1)) > Mathf.Abs(Vector3.Dot(toTable, axis2)))
        {
            forward = axis1; depth = size1; width = size2;
        }
        else
        {
            forward = axis2; depth = size2; width = size1;
        }
        if (Vector3.Dot(toTable, forward) < 0f) forward = -forward;   // on le retourne s'il pointe vers le joueur

        // --- 4. Aligner l'environnement ---
        // On tourne d'abord (ce qui déplace aussi TableAnchor), puis on déplace.
        Quaternion targetRot = Quaternion.LookRotation(forward, Vector3.up);
        m_environment.rotation = targetRot * Quaternion.Inverse(m_tableAnchor.rotation) * m_environment.rotation;
        m_environment.position += center - m_tableAnchor.position;

        // La fenêtre prend la taille de la table (le quad est couché : x = largeur, y = profondeur)
        m_window.localScale = new Vector3(width + 2f * m_margin, depth + 2f * m_margin, 1f);

        foreach (GameObject m in m_markers) Destroy(m);
        m_markers.Clear();
        m_instructions.gameObject.SetActive(false);
        m_environment.gameObject.SetActive(true);
        m_done = true;
        m_onCalibrated.Invoke();
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}