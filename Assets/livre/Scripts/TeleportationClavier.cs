using Oculus.Interaction.Locomotion;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Test sans casque : la touche T téléporte le joueur 1,5 m devant lui, sur le sol.
/// Passe par le même Locomotor que la téléportation à la main, donc ça vérifie
/// que le déplacement du rig marche. (Le geste de la main, lui, ne se teste qu'avec le casque.)
/// </summary>
public class TeleportationClavier : MonoBehaviour
{
    [Tooltip("Le Locomotor du rig Meta (FirstPersonLocomotor ou PlayerLocomotor).")]
    public MonoBehaviour locomoteur;
    [Tooltip("Tête du joueur (CenterEyeAnchor).")]
    public Transform tete;
    public float distance = 1.5f;
    [Tooltip("Hauteur du sol téléportable.")]
    public float hauteurSol = 0f;
    public bool actif = true;

    void Update()
    {
        var clavier = Keyboard.current;
        if (!actif || clavier == null || tete == null || !clavier.tKey.wasPressedThisFrame)
            return;
        if (locomoteur is not ILocomotionEventHandler gestionnaire)
            return;

        var avant = Vector3.ProjectOnPlane(tete.forward, Vector3.up);
        if (avant.sqrMagnitude < 0.001f)
            avant = Vector3.forward;
        var cible = tete.position + avant.normalized * distance;
        cible.y = hauteurSol;

        gestionnaire.HandleLocomotionEvent(new LocomotionEvent(0, new Pose(cible, Quaternion.identity),
            LocomotionEvent.TranslationType.Absolute, LocomotionEvent.RotationType.None));
    }
}
