using System.Collections.Generic;
using Oculus.Interaction.Input;
using Oculus.Interaction.Locomotion;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

// Au lancement, le joueur délimite sa table en pinçant 3 de ses coins, dans n'importe quel ordre.
// On aligne ensuite l'environnement virtuel dessus (rotation sur Y et déplacement à l'horizontale
// seulement : le sol virtuel reste au niveau du vrai sol) et on ouvre la fenêtre passthrough.
//
// Pas d'ancre spatiale : OVRSpatialAnchor recale son objet à chaque frame par rapport au rig,
// donc un décor accroché à l'ancre suivrait le joueur à chaque téléportation.
// À la place, on mémorise la pose du rig au moment du calibrage et on la remet exactement
// quand le joueur se téléporte sur HotspotBureau : la table virtuelle retombe alors sur la vraie.
//
// En jeu : le geste Menu de la main gauche ou un recentrage (bouton Meta maintenu) relance le calibrage.
public class TableCalibration : MonoBehaviour
{
    [Header("Mains et tête")]
    [SerializeField] private Hand[] m_hands;            // main gauche et main droite
    [SerializeField] private Transform m_head;          // CenterEyeAnchor : sert à savoir quel bord est face au joueur

    [Header("Scène")]
    [SerializeField] private Transform m_environment;   // racine de l'environnement virtuel
    [SerializeField] private Transform m_tableAnchor;   // centre du dessus du bureau virtuel (enfant de l'environnement), +Z vers le fond du bureau
    [SerializeField] private Transform m_window;        // le quad passthrough (enfant de TableAnchor)
    [SerializeField] private GameObject m_markerPrefab; // petite sphère posée sur chaque coin

    [Header("Instructions")]
    [SerializeField] private TMP_Text m_instructions;       // texte des étapes (TextMeshPro)
    [SerializeField] private GameObject m_instructionsPanel; // panneau entier (parchemin, halo...) affiché pendant le calibrage

    [Header("Téléportation")]
    [SerializeField] private Transform m_rig;                 // l'objet que la téléportation déplace (champ "Player Origin" du locomoteur)

    // Accepte n'importe quel composant qui implémente ILocomotionEventHandler :
    // FirstPersonLocomotor (ton PlayerController) ou PlayerLocomotor. On peut glisser le GameObject entier,
    // l'attribut Interface trouve le bon composant dessus. Vide = cherché automatiquement dans la scène.
    [SerializeField, Oculus.Interaction.Interface(typeof(ILocomotionEventHandler))]
    private UnityEngine.Object m_locomotor;
    [SerializeField] private Transform m_hotspotBureau;       // HotspotBureau
    [SerializeField] private float m_hotspotRadius = 1f;      // une téléportation qui arrive à moins de cette distance compte comme "au bureau"

    [Header("Test en éditeur (simulateur)")]
    // En éditeur uniquement : si les 3 champs sont remplis, leurs positions remplacent les pincements
    // et la table est validée tout de suite. Touche C : relancer. Sur le casque, ces champs sont ignorés.
    // Pour tester les vrais pincements via Link, il suffit de vider un des champs.
    [SerializeField] private Transform fakeCoin1;
    [SerializeField] private Transform fakeCoin2;
    [SerializeField] private Transform fakeCoin3;

    [Header("Réglages")]
    [SerializeField] private float m_margin = 0.05f;    // marge autour de la table, en mètres

    [Header("Événements")]
    [SerializeField] private UnityEvent m_onCalibrated; // appelé quand la table est validée

    private readonly List<Vector3> m_points = new List<Vector3>();
    private readonly List<GameObject> m_markers = new List<GameObject>();
    private bool[] m_wasPinching;   // la main pinçait-elle à l'image précédente ?
    private bool m_done;
    private Pose m_calibratedRigPose; // pose du rig au moment où la table a été validée
    private ILocomotionEventHandler m_locomotorHandler; // m_locomotor vu à travers son interface
    private bool m_restorePending;    // une téléportation vers HotspotBureau attend qu'on remette le rig en place

    private void OnEnable()
    {
        // Recentrage (bouton Meta maintenu) : le casque redéfinit son repère, la vraie table "bouge"
        // par rapport au virtuel. Le plus fiable est de recalibrer.
        if (OVRManager.display != null) OVRManager.display.RecenteredPose += Restart;

        // Champ vide : on prend le FirstPersonLocomotor de la scène, sinon un PlayerLocomotor.
        if (m_locomotor == null) m_locomotor = FindFirstObjectByType<FirstPersonLocomotor>();
        if (m_locomotor == null) m_locomotor = FindFirstObjectByType<PlayerLocomotor>();

        m_locomotorHandler = m_locomotor as ILocomotionEventHandler;
        if (m_locomotorHandler != null) m_locomotorHandler.WhenLocomotionEventHandled += OnLocomotion;
        else Debug.LogWarning("TableCalibration : aucun locomoteur trouvé, le retour au bureau ne sera pas recalé.");
    }

    private void OnDisable()
    {
        if (OVRManager.display != null) OVRManager.display.RecenteredPose -= Restart;
        if (m_locomotorHandler != null) m_locomotorHandler.WhenLocomotionEventHandled -= OnLocomotion;
    }

    private static readonly string[] Steps =
    {
        "Délimite ta zone de travail. <b> Pince un coin de ta table <b>",
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
        foreach (GameObject m in m_markers) Destroy(m);
        m_markers.Clear();
        m_points.Clear();
        m_done = false;
        m_restorePending = false;
        m_environment.gameObject.SetActive(false);   // on cache le virtuel : le joueur voit sa vraie table
        AfficherInstructions(true);
        if (m_instructions != null) m_instructions.text = Steps[0];

        if (UseFakeCorners()) CalibrateWithFakeCorners();
    }

    // Vrai seulement dans l'éditeur Unity ET si les 3 faux coins sont renseignés.
    // Application.isEditor vaut toujours false dans un build installé sur le casque.
    private bool UseFakeCorners() =>
        Application.isEditor && fakeCoin1 != null && fakeCoin2 != null && fakeCoin3 != null;

    // Fait exactement ce que feraient 3 pincements, puis valide sans attendre le 4e pincement.
    private void CalibrateWithFakeCorners()
    {
        m_points.Add(fakeCoin1.position);
        m_points.Add(fakeCoin2.position);
        m_points.Add(fakeCoin3.position);
        Validate();
    }

    private void Update()
    {
        RestoreRigIfPending();

        // Touche C (éditeur uniquement) : relancer le calibrage, donc avec les faux coins s'ils sont remplis.
        if (Application.isEditor && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) Restart();

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
        if (m_instructions != null) m_instructions.text = Steps[m_points.Count];
    }

    private void Validate()
    {
        // On calcule à plat (sans la hauteur) : la hauteur des coins pincés n'est plus utilisée,
        // c'est le modèle 3D du bureau qui est réglé à la hauteur de la vraie table.
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

        // --- 4. Aligner l'environnement (rotation sur Y uniquement) ---
        // Angle entre l'avant actuel de TableAnchor (à plat) et l'avant de la vraie table.
        // On ne garde que cet angle autour de l'axe vertical : le sol virtuel ne penche jamais,
        // même si TableAnchor est un peu incliné dans la scène.
        float angle = Vector3.SignedAngle(Flat(m_tableAnchor.forward), forward, Vector3.up);
        m_environment.rotation = Quaternion.AngleAxis(angle, Vector3.up) * m_environment.rotation;

        // --- 5. Déplacer l'environnement (à l'horizontale uniquement) ---
        // La rotation a déplacé TableAnchor, on mesure donc l'écart APRÈS avoir tourné.
        // Écart en Y mis à 0 : l'environnement garde sa hauteur, le sol virtuel reste au niveau du vrai sol.
        Vector3 offset = center - m_tableAnchor.position;
        offset.y = 0f;
        m_environment.position += offset;

        // La fenêtre prend la taille de la table (le quad est couché : x = largeur, y = profondeur)
        m_window.localScale = new Vector3(width + 2f * m_margin, depth + 2f * m_margin, 1f);

        // --- 6. Mémoriser la pose du rig ---
        // Le virtuel colle au réel tant que le rig est à cette pose. Si le joueur part à la bibliothèque
        // puis revient sur HotspotBureau, on remettra le rig exactement ici (voir OnLocomotion).
        if (m_rig != null) m_calibratedRigPose = new Pose(m_rig.position, m_rig.rotation);

        foreach (GameObject m in m_markers) Destroy(m);
        m_markers.Clear();
        AfficherInstructions(false);
        m_environment.gameObject.SetActive(true);
        m_done = true;
        m_onCalibrated.Invoke();
    }

    // Appelé par le locomoteur quand il a traité un déplacement (téléportation, rotation...).
    // Attention : FirstPersonLocomotor envoie cet événement AVANT de déplacer le rig. Il déplace d'abord
    // sa capsule (CharacterController), puis en toute fin de frame il fait "rattraper" la capsule au rig.
    // Si on bougeait le rig ici, ce rattrapage s'ajouterait par-dessus. On note donc juste qu'il faudra
    // remettre le rig en place, et on le fait à la frame suivante (voir RestoreRigIfPending).
    private void OnLocomotion(LocomotionEvent evt, Pose delta)
    {
        if (!m_done || m_rig == null || m_hotspotBureau == null) return;

        // Seules les téléportations nous intéressent : elles ont une position d'arrivée "absolue".
        // Les rotations sur place (snap turn) ont une translation None et sont ignorées.
        if (evt.Translation != LocomotionEvent.TranslationType.Absolute &&
            evt.Translation != LocomotionEvent.TranslationType.AbsoluteEyeLevel) return;

        // Le point d'arrivée est-il HotspotBureau ? (comparaison à plat)
        if (Vector3.Distance(Flat(evt.Pose.position), Flat(m_hotspotBureau.position)) > m_hotspotRadius) return;

        m_restorePending = true;
    }

    // Le dessus du bureau virtuel (utilisé par FinDuJeu pour poser l'image).
    public Transform TableAnchor => m_tableAnchor;

    // Remet tout de suite le rig à la pose mémorisée au calibrage : la table virtuelle retombe sur la vraie.
    // Utilisée par FinDuJeu. Ne fait rien si la table n'a pas encore été calibrée.
    // Attention : avec FirstPersonLocomotor actif, sa capsule doit pouvoir suivre ;
    // FinDuJeu désactive donc le locomoteur AVANT d'appeler cette méthode.
    public void RamenerAuBureau()
    {
        if (!m_done || m_rig == null) return;
        m_restorePending = false;
        m_rig.SetPositionAndRotation(m_calibratedRigPose.position, m_calibratedRigPose.rotation);
    }

    // Exécuté au début de la frame qui suit la téléportation : le locomoteur a fini de bouger le rig.
    private void RestoreRigIfPending()
    {
        if (!m_restorePending) return;
        m_restorePending = false;

        // On remet le rig là où il était au calibrage : la table virtuelle retombe sur la vraie.
        // La capsule du FirstPersonLocomotor suit toute seule : à chaque Update, elle se recale sous la tête.
        m_rig.SetPositionAndRotation(m_calibratedRigPose.position, m_calibratedRigPose.rotation);
    }

    // Affiche / cache le panneau entier s'il est renseigné, sinon juste le texte.
    private void AfficherInstructions(bool visible)
    {
        if (m_instructionsPanel != null)
            m_instructionsPanel.SetActive(visible);
        else if (m_instructions != null)
            m_instructions.gameObject.SetActive(visible);
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}