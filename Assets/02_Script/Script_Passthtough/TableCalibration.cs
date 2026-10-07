using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Au lancement, le joueur délimite sa table en pinçant 3 points.
// On aligne ensuite l'environnement virtuel dessus et on ouvre la fenêtre passthrough.
public class TableCalibration : MonoBehaviour
{
    [Header("Mains")]
    [SerializeField] private Hand[] m_hands;            // main gauche et main droite

    [Header("Scène")]
    [SerializeField] private Transform m_environment;   // racine de l'environnement virtuel
    [SerializeField] private Transform m_tableAnchor;   // dessus de la table virtuelle (enfant de l'environnement)
    [SerializeField] private Transform m_window;        // le quad passthrough (enfant de TableAnchor)
    [SerializeField] private GameObject m_markerPrefab; // petite sphère posée sur chaque point
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
        "Pince le coin avant gauche de ta table",
        "Pince le coin avant droit",
        "Pince un point du bord du fond",
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
        Vector3 a = m_points[0], b = m_points[1], c = m_points[2];
        float height = (a.y + b.y + c.y) / 3f;   // hauteur de la table = moyenne des 3 points

        // On calcule à plat (sans la hauteur) pour avoir un rectangle bien horizontal
        Vector3 a2 = Flat(a), b2 = Flat(b), c2 = Flat(c);
        Vector3 right = (b2 - a2).normalized;                 // direction du bord avant
        Vector3 forward = Vector3.Cross(right, Vector3.up);   // perpendiculaire : vers le fond de la table
        if (Vector3.Dot(c2 - a2, forward) < 0f) forward = -forward;   // si les coins ont été pincés dans l'autre ordre

        float width = Vector3.Distance(a2, b2);
        float depth = Vector3.Dot(c2 - a2, forward);          // distance entre le bord avant et le point du fond
        Vector3 center = (a2 + b2) / 2f + forward * depth / 2f;
        center.y = height;

        // 1. On tourne l'environnement pour que TableAnchor soit orienté comme la vraie table
        Quaternion targetRot = Quaternion.LookRotation(forward, Vector3.up);
        m_environment.rotation = targetRot * Quaternion.Inverse(m_tableAnchor.rotation) * m_environment.rotation;
        // 2. Puis on le déplace pour que TableAnchor tombe pile au centre de la vraie table
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
