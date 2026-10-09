using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Message affiché après Play : apparaît doucement devant le joueur et reste affiché
/// jusqu'à ce que le joueur clique (bouton relié à Fermer()). Il s'efface alors,
/// puis prévient le menu pour lancer la suite (calibration, etc.).
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class MessageMystere : MonoBehaviour
{
    [Header("Test au clavier (sans casque)")]
    [Tooltip("Entrée : fermer le message.")]
    public bool raccourcisClavier = true;

    [Header("Placement devant le joueur")]
    [Tooltip("Distance du message devant la tête, en mètres (le menu est à 0.55).")]
    public float distanceDevant = 0.55f;
    [Tooltip("De combien le message est plus bas que les yeux, en mètres (le menu est à 0.2).")]
    public float sousLesYeux = 0.2f;

    [Header("Fin du message")]
    [Tooltip("Désactivé au moment exact où le message finit de disparaître (ex : la sphère FondMenu). Vide = rien.")]
    public GameObject fondADesactiver;

    CanvasGroup m_Groupe;
    float m_DureeFondu = 2f;
    bool m_PeutFermer;       // vrai une fois le message complètement apparu
    bool m_EnTrainDeFermer;
    Action m_QuandFerme;     // ce qu'on fait une fois le message disparu

    public void Afficher(MenuDepart menu, Action quandFerme)
    {
        m_Groupe = GetComponent<CanvasGroup>();
        m_Groupe.alpha = 0f;
        m_Groupe.interactable = true;
        m_Groupe.blocksRaycasts = true;

        m_DureeFondu = menu.dureeFonduMessage;
        m_QuandFerme = quandFerme;
        m_PeutFermer = false;
        m_EnTrainDeFermer = false;

        gameObject.SetActive(true);
        menu.PlacerDevant(transform, distanceDevant, sousLesYeux);
        Debug.Log($"[DBG] MessageMystere.Afficher : actif = {gameObject.activeInHierarchy}, position = {transform.position}, rotation = {transform.eulerAngles}"); // [DBG] temporaire
        StopAllCoroutines();
        StartCoroutine(Apparaitre());
    }

    // [DBG] temporaire : montre QUAND le message est activé / désactivé, même si ce n'est pas par Afficher()
    void OnEnable() => Debug.Log($"[DBG] MessageMystere OnEnable : position = {transform.position}, alpha = {GetComponent<CanvasGroup>().alpha}");
    void OnDisable() => Debug.Log("[DBG] MessageMystere OnDisable");

    /// <summary>À relier au OnClick du bouton du message.</summary>
    public void Fermer()
    {
        // On ignore les clics pendant l'apparition ou si on est déjà en train de fermer.
        if (!m_PeutFermer || m_EnTrainDeFermer)
            return;
        m_EnTrainDeFermer = true;
        StartCoroutine(Disparaitre());
    }

    void Update()
    {
        var clavier = Keyboard.current;
        if (!raccourcisClavier || clavier == null)
            return;
        if (clavier.enterKey.wasPressedThisFrame || clavier.numpadEnterKey.wasPressedThisFrame)
            Fermer();
    }

    IEnumerator Apparaitre()
    {
        yield return Fondu(0f, 1f, m_DureeFondu);
        m_PeutFermer = true;
    }

    IEnumerator Disparaitre()
    {
        m_Groupe.interactable = false;
        yield return Fondu(1f, 0f, m_DureeFondu * 0.6f);

        var suite = m_QuandFerme;
        m_QuandFerme = null;
        suite?.Invoke();

        if (fondADesactiver != null)
            fondADesactiver.SetActive(false);
        gameObject.SetActive(false);
    }

    IEnumerator Fondu(float de, float vers, float duree)
    {
        for (var t = 0f; t < duree; t += Time.unscaledDeltaTime)
        {
            m_Groupe.alpha = Mathf.Lerp(de, vers, t / duree);
            yield return null;
        }
        m_Groupe.alpha = vers;
    }
}