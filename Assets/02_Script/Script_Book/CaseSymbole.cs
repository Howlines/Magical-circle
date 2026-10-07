using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Une case de symbole posée sur une page (le carré vide en bas de page).
/// Ce script va sur la RACINE du prefab de case. Le prefab contient :
///  - le bouton poke ISDK rendu invisible (avec son Poke Interactable),
///  - un SpriteRenderer qui affiche le symbole choisi.
/// Le poke appelle Poke() via l'Interactable Unity Event Wrapper (When Select).
/// C'est SymbolesLivre qui crée, place, remplit et active les cases : ce script ne décide rien.
/// </summary>
public class CaseSymbole : MonoBehaviour
{
    [Tooltip("Le SpriteRenderer qui affiche le symbole dans le carré.")]
    public SpriteRenderer rendu;

    [Header("Aperçus (juste esthétique, optionnels)")]
    [Tooltip("Petit symbole à gauche : celui d'avant dans la liste. Règle sa taille et sa transparence (Color) dans le prefab.")]
    public SpriteRenderer precedent;
    [Tooltip("Petit symbole à droite : celui d'après dans la liste.")]
    public SpriteRenderer suivant;

    [Tooltip("Le composant Poke Interactable du bouton (dans les enfants du prefab).")]
    public PokeInteractable poke;

    // Remplis par SymbolesLivre à chaque image : quelle case de quelle page on affiche en ce moment.
    // (Une même case 3D peut montrer la page 2, puis la page 4 après un tour de page.)
    [System.NonSerialized] public SymbolesLivre gestionnaire;
    [System.NonSerialized] public SymbolesLivre.Case donnees;

    /// <summary>À brancher sur "When Select" de l'Interactable Unity Event Wrapper.</summary>
    public void Poke()
    {
        if (gestionnaire != null && donnees != null)
            gestionnaire.Avancer(donnees);
    }

    /// <summary>Affiche le symbole choisi au centre, et les symboles d'avant / d'après en aperçu.</summary>
    public void AfficherSymboles(Sprite actuel, Sprite avant, Sprite apres)
    {
        Afficher(rendu, actuel);
        Afficher(precedent, avant);
        Afficher(suivant, apres);
    }

    static void Afficher(SpriteRenderer cible, Sprite sprite)
    {
        if (cible == null)
            return;

        cible.sprite = sprite;
        cible.enabled = sprite != null; // pas de symbole = rien d'affiché
    }

    public void AutoriserPoke(bool autorise)
    {
        if (poke != null && poke.enabled != autorise)
            poke.enabled = autorise;
    }
}