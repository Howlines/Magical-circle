using System.Collections;
using UnityEngine;

/// <summary>
/// Message affiché après Start : apparaît doucement devant le joueur, reste quelques secondes, puis s'efface.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class MessageMystere : MonoBehaviour
{
    CanvasGroup m_Groupe;

    public void Afficher(MenuDepart menu)
    {
        m_Groupe = GetComponent<CanvasGroup>();
        m_Groupe.alpha = 0f;
        gameObject.SetActive(true);
        menu.PlacerDevant(transform, menu.distanceMessage, 0f);
        StopAllCoroutines();
        StartCoroutine(Animer(menu.dureeFonduMessage, menu.dureeAffichageMessage));
    }

    IEnumerator Animer(float fondu, float affichage)
    {
        yield return Fondu(0f, 1f, fondu);
        yield return new WaitForSeconds(affichage);
        yield return Fondu(1f, 0f, fondu);
        gameObject.SetActive(false);
    }

    IEnumerator Fondu(float de, float vers, float duree)
    {
        for (var t = 0f; t < duree; t += Time.deltaTime)
        {
            m_Groupe.alpha = Mathf.Lerp(de, vers, t / duree);
            yield return null;
        }
        m_Groupe.alpha = vers;
    }
}
