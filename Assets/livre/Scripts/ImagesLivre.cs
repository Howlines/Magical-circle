using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Liste des images des pages du livre. LivreVR garde ses "pages" sous forme de texte :
/// chaque texte est le nom d'une image de cette liste, et ImagePageLivre affiche l'image correspondante.
/// </summary>
public class ImagesLivre : MonoBehaviour
{
    [Tooltip("Images des pages, dans l'ordre. Les noms doivent correspondre à la liste Pages de LivreVR.")]
    public Texture2D[] images;

    Dictionary<string, Texture2D> m_ParNom;

    public Texture2D Trouver(string nom)
    {
        if (string.IsNullOrEmpty(nom) || images == null)
            return null;

        if (m_ParNom == null)
        {
            m_ParNom = new Dictionary<string, Texture2D>();
            foreach (var image in images)
            {
                if (image != null)
                    m_ParNom[image.name] = image;
            }
        }

        return m_ParNom.TryGetValue(nom, out var texture) ? texture : null;
    }
}
