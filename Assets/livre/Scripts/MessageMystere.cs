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
        menu.PlacerDevant(transform, menu.distanceMessage, 0f);
        StopAllCoroutines();
        StartCoroutine(Apparaitre());
    }

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