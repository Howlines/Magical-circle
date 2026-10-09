using UnityEngine;

public class Flotter : MonoBehaviour
{
    public float hauteur = 0.02f;    // amplitude en mètres (2 cm)
    public float vitesse = 1.5f;     // vitesse du mouvement
    public float rotation = 0f;      // degrés par seconde, 0 = ne tourne pas

    Vector3 m_depart;

    void Start()
    {
        m_depart = transform.localPosition;
    }

    void Update()
    {
        float decalage = Mathf.Sin(Time.time * vitesse) * hauteur;
        transform.localPosition = m_depart + Vector3.up * decalage;

        if (rotation != 0f)
            transform.Rotate(0f, rotation * Time.deltaTime, 0f, Space.World);
    }
}