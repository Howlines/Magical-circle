using TMPro;
using UnityEngine;

/// <summary>
/// Affiche l'image d'une page à la place d'un texte du livre, sans toucher à la mécanique de LivreVR :
/// LivreVR continue d'écrire dans le texte (le nom de l'image) et de l'activer / désactiver,
/// ce script recopie simplement ça sur l'image (le texte lui-même est rendu invisible).
/// </summary>
[RequireComponent(typeof(Renderer))]
public class ImagePageLivre : MonoBehaviour
{
    static readonly int k_MainTex = Shader.PropertyToID("_MainTex");

    [Tooltip("Le texte du livre que cette image remplace.")]
    public TMP_Text texteSource;

    [Tooltip("La liste des images (sur l'objet Livre VR).")]
    public ImagesLivre images;

    Renderer m_Renderer;
    MaterialPropertyBlock m_Bloc;
    string m_NomAffiche;
    Texture2D m_Texture;

    void Awake()
    {
        m_Renderer = GetComponent<Renderer>();
        m_Bloc = new MaterialPropertyBlock();
    }

    // LateUpdate : passe après LivreVR (Update et coroutines), donc on voit toujours le texte à jour.
    void LateUpdate()
    {
        var visible = texteSource != null && texteSource.enabled;
        var nom = visible ? texteSource.text : null;

        if (nom != m_NomAffiche)
        {
            m_NomAffiche = nom;
            m_Texture = images != null ? images.Trouver(nom) : null;
            if (m_Texture != null)
            {
                m_Renderer.GetPropertyBlock(m_Bloc);
                m_Bloc.SetTexture(k_MainTex, m_Texture);
                m_Renderer.SetPropertyBlock(m_Bloc);
            }
        }

        m_Renderer.enabled = visible && m_Texture != null;
    }
}
