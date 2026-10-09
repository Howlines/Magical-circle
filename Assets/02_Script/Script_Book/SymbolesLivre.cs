using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Gère les cases de symboles du livre : on poke une case pour faire défiler les symboles.
///
/// Une page est VALIDÉE quand ses cases ont le bon symbole ET que le joueur a déjà lancé le sort de cette page
/// au moins une fois (SortLance). Comme ça, on ne peut pas trouver la réponse au pif sans avoir testé le sort.
/// Une page validée affiche son symbole dans un petit cercle de la page récap. On peut toujours la changer :
/// si le symbole n'est plus le bon, la page n'est plus validée et son cercle se vide.
/// Quand TOUTES les pages sont validées : onToutResolu se déclenche et la page à débloquer change d'image.
///
/// Même astuce que pour les textes de LivreVR : le livre n'a que 4 surfaces (gauche, recto, verso,
/// sous la feuille). Chaque surface a quelques objets 3D, et à chaque image on regarde quelle page
/// la surface affiche pour y mettre les bons. L'état (quel symbole est choisi) est stocké
/// dans les DONNÉES de la page, pas dans les objets 3D : il survit aux tours de page.
/// </summary>
public class SymbolesLivre : MonoBehaviour
{
    [Serializable]
    public class Case
    {
        [Tooltip("Position sur la page. 0,0 = coin bas gauche, 1,1 = coin haut droit, 0.5,0.5 = centre.")]
        public Vector2 position = new Vector2(0.5f, 0.19f);

        [Tooltip("Index du BON symbole dans la liste Symboles (0 = le premier).")]
        public int solution;

        [NonSerialized] public int actuel = -1; // -1 = case vide
    }

    [Serializable]
    public class PageSymboles
    {
        [Tooltip("Numéro de la page dans LivreVR.pages (0 = première page, 1 = deuxième...).")]
        public int page;

        [Tooltip("Nom du glyphe de cette page, comme GlyphReader l'envoie (ex : water, fire, earth, wind, light). Vide = pas besoin de lancer le sort.")]
        public string nomSort;

        [Tooltip("Où afficher le symbole de cette page sur la page récap (son petit cercle). 0,0 = bas gauche, 1,1 = haut droite.")]
        public Vector2 positionRecap = new Vector2(0.5f, 0.73f);

        public Case[] cases = { new Case() };
    }

    [Header("Références")]
    public LivreVR livre;
    [Tooltip("Le prefab de case (bouton poke invisible + SpriteRenderer + script CaseSymbole).")]
    public CaseSymbole prefabCase;

    [Header("Contenu")]
    [Tooltip("Tous les symboles simplifiés, dans l'ordre de défilement.")]
    public Sprite[] symboles;
    public PageSymboles[] pagesSymboles;

    [Header("Page récap (les petits cercles)")]
    [Tooltip("Numéro de la page qui a les petits cercles (dans LivreVR.pages). -1 = pas de page récap.")]
    public int pageRecap = 24;
    [Tooltip("Taille des symboles dans les petits cercles, par rapport à la case (0.3 = 30 %).")]
    public float echelleRecap = 0.3f;

    [Header("Déblocage final")]
    [Tooltip("Numéro de la page qui change quand tout est validé. -1 = aucune.")]
    public int pageADebloquer = 24;
    [Tooltip("Nouveau contenu de cette page : le NOM de la nouvelle image (comme dans la liste Pages de LivreVR).")]
    public string nouveauContenu;
    [Tooltip("Décoché : les petits cercles disparaissent quand la page a changé (si la nouvelle image les dessine déjà).")]
    public bool garderRecapApresDeblocage = true;

    [Header("Placement")]
    [Tooltip("Hauteur au-dessus de la page (mètres) pour que la case ne soit pas enfoncée dans le papier.")]
    public float decalage = 0.002f;
    [Tooltip("Rotation ajoutée à chaque case. La racine du prefab est posée avec son axe Y (vert) qui sort de la page : si le bouton est couché ou tourné, ajuste ici.")]
    public Vector3 rotationCase;
    [Tooltip("Multiplie la taille du prefab.")]
    public float echelle = 1f;
    [Tooltip("Coche si la case apparaît en miroir à gauche au lieu de droite (ou l'inverse).")]
    public bool inverserGaucheDroite;
    [Tooltip("Coche si la case apparaît en haut de la page au lieu d'en bas.")]
    public bool inverserHautBas;
    [Tooltip("Coche si la case est derrière la page (invisible) au lieu de devant.")]
    public bool inverserCote;

    [Header("Règles")]
    [Tooltip("Coché : les cases commencent vides. Décoché : elles commencent sur le premier symbole.")]
    public bool commencerVide = true;
    [Tooltip("Coché : sur une page validée, les aperçus (symboles d'avant / d'après) disparaissent. La case reste pokable.")]
    public bool masquerApercusQuandValide;

    [Header("Son (optionnel)")]
    public AudioSource sonPoke;
    public AudioSource sonPageValidee;
    public AudioSource sonResolu;

    [Header("Événements")]
    [Tooltip("Une page vient d'être validée (le numéro de la page est passé en paramètre).")]
    public UnityEvent<int> onPageValidee;
    [Tooltip("Une page validée vient de redevenir fausse (on a changé son symbole).")]
    public UnityEvent<int> onPageInvalidee;
    [Tooltip("Toutes les pages sont validées.")]
    public UnityEvent onToutResolu;

    [Header("Test au clavier (sans casque)")]
    [Tooltip("P / M : poke la case de la page de gauche / droite. K / L : lance le sort de la page de gauche / droite.")]
    public bool raccourcisClavier = true;

    public bool EstResolu { get; private set; }

    // Les cases 3D et les symboles récap de chaque surface (index = LivreVR.Surface).
    readonly List<CaseSymbole>[] m_Cases = new List<CaseSymbole>[4];
    readonly List<Transform>[] m_Recaps = new List<Transform>[4];
    readonly List<SpriteRenderer>[] m_RecapsRendus = new List<SpriteRenderer>[4];

    readonly Dictionary<int, PageSymboles> m_ParPage = new Dictionary<int, PageSymboles>();
    readonly Dictionary<Case, PageSymboles> m_PageDeLaCase = new Dictionary<Case, PageSymboles>();
    readonly HashSet<string> m_SortsLances = new HashSet<string>();
    readonly HashSet<PageSymboles> m_PagesValidees = new HashSet<PageSymboles>();

    void Awake()
    {
        foreach (var p in pagesSymboles)
        {
            if (m_ParPage.ContainsKey(p.page))
                Debug.LogWarning($"SymbolesLivre : la page {p.page} est définie deux fois.", this);
            m_ParPage[p.page] = p;

            foreach (var c in p.cases)
            {
                m_PageDeLaCase[c] = p;
                c.actuel = commencerVide ? -1 : 0;
                if (c.solution < 0 || c.solution >= symboles.Length)
                    Debug.LogWarning($"SymbolesLivre : page {p.page}, solution {c.solution} hors de la liste des symboles.", this);
            }
        }
    }

    void Start()
    {
        if (livre == null || prefabCase == null)
        {
            Debug.LogError("SymbolesLivre : remplis les champs Livre et Prefab Case dans l'Inspector.", this);
            enabled = false;
            return;
        }

        // Combien de cases au maximum sur une page ? On crée ce nombre de cases sur chaque surface.
        var max = 0;
        foreach (var p in pagesSymboles)
            max = Mathf.Max(max, p.cases.Length);

        if (max == 0)
            Debug.LogWarning("SymbolesLivre : aucune case à créer. Ajoute des éléments dans Pages Symboles (avec au moins 1 case chacun).", this);

        // Les images de pages (ImagePageLivre) : on s'en sert de "feuille" pour placer les cases.
        var imagesPages = FindObjectsByType<ImagePageLivre>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (var s = 0; s < 4; s++)
        {
            var surface = (LivreVR.Surface)s;
            m_Supports[s] = TrouverSupport(livre.TexteDe(surface), imagesPages);

            // Pas de parent : la page a une échelle non uniforme (plus haute que large), et un enfant
            // tourné sous un parent comme ça se fait déformer par Unity. Comme on replace tout
            // à chaque image (LateUpdate), ça suit quand même la page.
            m_Cases[s] = new List<CaseSymbole>();
            for (var k = 0; k < max; k++)
            {
                var c = Instantiate(prefabCase);
                c.name = $"Case {surface} {k + 1}";
                c.gestionnaire = this;
                c.gameObject.SetActive(false);
                m_Cases[s].Add(c);
            }

            // Symboles récap : une copie du symbole du prefab (même matériau, même couleur, même orientation)
            // dans un support vide, un par page de symbole.
            m_Recaps[s] = new List<Transform>();
            m_RecapsRendus[s] = new List<SpriteRenderer>();
            if (pageRecap >= 0 && prefabCase.rendu != null)
            {
                for (var k = 0; k < pagesSymboles.Length; k++)
                {
                    var support = new GameObject($"Recap {surface} {k + 1}").transform;
                    var rendu = Instantiate(prefabCase.rendu, support); // garde sa position / rotation du prefab
                    support.gameObject.SetActive(false);
                    m_Recaps[s].Add(support);
                    m_RecapsRendus[s].Add(rendu);
                }
            }
        }

        Debug.Log($"SymbolesLivre : {max} case(s) créée(s) sur chacune des 4 surfaces.", this);
    }

    // --- Lancer un sort ------------------------------------------------------------------------

    /// <summary>
    /// À appeler par le jeu quand le joueur lance un sort (ex : SortLance("feu")).
    /// Le nom doit être le même que le "Nom Sort" d'une page (majuscules / espaces ignorés).
    /// </summary>
    public void SortLance(string nomSort)
    {
        Debug.Log($"[DBG] SymbolesLivre.SortLance(\"{nomSort}\") reçu", this); // [DBG] temporaire
        var nom = Normaliser(nomSort);
        if (string.IsNullOrEmpty(nom) || !m_SortsLances.Add(nom))
            return; // déjà lancé avant : rien de nouveau

        Debug.Log($"SymbolesLivre : sort \"{nom}\" lancé pour la première fois.", this);
        VerifierTout(); // si la case de ce sort était déjà bonne, la page se valide maintenant
    }

    /// <summary>
    /// Même chose, mais avec la signature de l'événement "On Spell" de GlyphReader (string, Vector3) :
    /// on peut la brancher directement dessus dans l'Inspector, sans toucher au code. La position est ignorée.
    /// </summary>
    public void SortLance(string glyphe, Vector3 position) => SortLance(glyphe);

    public bool SortDejaLance(string nomSort)
    {
        var nom = Normaliser(nomSort);
        return string.IsNullOrEmpty(nom) || m_SortsLances.Contains(nom); // pas de sort = pas de condition
    }

    static string Normaliser(string nom) => string.IsNullOrWhiteSpace(nom) ? null : nom.Trim().ToLowerInvariant();

    // --- Où poser les cases --------------------------------------------------------------------
    // Une "feuille" = l'objet qui affiche la page. On préfère l'image (ImagePageLivre), parce que
    // c'est elle qu'on voit : la case tombera pile dans le carré dessiné. Sinon, on prend le texte.

    class Support
    {
        public Transform transform;
        public Vector3 min;        // coin de la feuille (repère local)
        public Vector3 axeLargeur; // direction + longueur de la largeur (repère local)
        public Vector3 axeHauteur; // direction + longueur de la hauteur (repère local)
        public Vector3 normale;    // côté lisible de la page (repère local)
    }

    readonly Support[] m_Supports = new Support[4];

    Support TrouverSupport(TMPro.TMP_Text texte, ImagePageLivre[] imagesPages)
    {
        foreach (var image in imagesPages)
        {
            if (image.texteSource != texte)
                continue;

            var filtre = image.GetComponent<MeshFilter>();
            if (filtre == null)
                break;

            // ImagePageCourbee (sur le recto / verso) retire le mesh du Quad au lancement pour afficher
            // sa copie courbée à la place. Dans ce cas, on reprend la taille d'un Quad Unity standard
            // (de -0.5 à 0.5 sur X et Y), sinon la case serait placée au mauvais endroit.
            var b = filtre.sharedMesh != null
                ? filtre.sharedMesh.bounds
                : new Bounds(Vector3.zero, new Vector3(1f, 1f, 0f));

            // On regarde la boîte du mesh : l'axe le plus fin est l'épaisseur (la normale),
            // les deux autres sont la largeur et la hauteur de la page.
            var support = new Support { transform = image.transform, min = b.min };

            if (b.size.y < b.size.x && b.size.y < b.size.z)
            {
                // Mesh "Plane" de Unity : à plat sur X/Z, on le voit depuis +Y.
                support.axeLargeur = new Vector3(b.size.x, 0f, 0f);
                support.axeHauteur = new Vector3(0f, 0f, b.size.z);
                support.normale = Vector3.up;
            }
            else
            {
                // Mesh "Quad" de Unity : debout sur X/Y, on le voit depuis -Z.
                support.axeLargeur = new Vector3(b.size.x, 0f, 0f);
                support.axeHauteur = new Vector3(0f, b.size.y, 0f);
                support.normale = Vector3.back;
            }
            return support;
        }

        // Pas d'image trouvée : on se sert du rectangle du texte. Un texte TMP se lit depuis -Z.
        var r = texte.rectTransform.rect;
        return new Support
        {
            transform = texte.transform,
            min = new Vector3(r.xMin, r.yMin, 0f),
            axeLargeur = new Vector3(r.width, 0f, 0f),
            axeHauteur = new Vector3(0f, r.height, 0f),
            normale = Vector3.back,
        };
    }

    void Update()
    {
        var clavier = Keyboard.current;
        if (!raccourcisClavier || clavier == null)
            return;

        if (Appuye(clavier, "p"))
            PokePremiereCase(LivreVR.Surface.Gauche);
        else if (Appuye(clavier, "m"))
            PokePremiereCase(LivreVR.Surface.Recto);
        else if (Appuye(clavier, "k"))
            LancerSortDeLaPage(LivreVR.Surface.Gauche);
        else if (Appuye(clavier, "l"))
            LancerSortDeLaPage(LivreVR.Surface.Recto);
    }

    // L'Input System repère les touches par leur PLACE sur un clavier QWERTY (clavier.mKey = la touche ","
    // sur un AZERTY). On cherche donc la touche par la lettre écrite dessus, selon la langue du clavier.
    static bool Appuye(Keyboard clavier, string lettre)
    {
        var touche = clavier.FindKeyOnCurrentKeyboardLayout(lettre);
        return touche != null && touche.wasPressedThisFrame;
    }

    // LateUpdate : après LivreVR, pour voir les textes déjà mis à jour cette image-ci.
    void LateUpdate()
    {
        // On ne poke que livre ouvert, pas pendant un tour de page, et seulement sur les 2 pages posées.
        var pokePossible = livre.EstOuvert && !livre.EstOccupe;

        for (var s = 0; s < 4; s++)
        {
            var surface = (LivreVR.Surface)s;
            var texte = livre.TexteDe(surface);
            var visible = texte.enabled && texte.gameObject.activeInHierarchy;
            var page = livre.PageSur(surface);

            // --- Les cases à poker ---
            m_ParPage.TryGetValue(page, out var donneesPage);
            var pageValidee = donneesPage != null && m_PagesValidees.Contains(donneesPage);
            var pokeSurface = pokePossible && (surface == LivreVR.Surface.Gauche || surface == LivreVR.Surface.Recto);

            for (var k = 0; k < m_Cases[s].Count; k++)
            {
                var c = m_Cases[s][k];
                var active = visible && donneesPage != null && k < donneesPage.cases.Length;

                if (c.gameObject.activeSelf != active)
                    c.gameObject.SetActive(active);
                if (!active)
                {
                    c.donnees = null;
                    continue;
                }

                var donnees = donneesPage.cases[k];
                c.donnees = donnees;
                Placer(c.transform, m_Supports[s], donnees.position, echelle);

                var apercus = !(pageValidee && masquerApercusQuandValide);
                c.AfficherSymboles(
                    Symbole(donnees.actuel),
                    apercus ? Symbole(Voisin(donnees.actuel, -1)) : null,
                    apercus ? Symbole(Voisin(donnees.actuel, +1)) : null);

                // Seule la résolution finale bloque les cases : une page validée reste modifiable.
                c.AutoriserPoke(pokeSurface && !EstResolu);
            }

            // --- Les petits cercles de la page récap ---
            var recapVisible = visible && page == pageRecap && (garderRecapApresDeblocage || !EstResolu);
            for (var k = 0; k < m_Recaps[s].Count; k++)
            {
                var p = pagesSymboles[k];
                var active = recapVisible && m_PagesValidees.Contains(p) && p.cases.Length > 0;

                var support = m_Recaps[s][k];
                if (support.gameObject.activeSelf != active)
                    support.gameObject.SetActive(active);
                if (!active)
                    continue;

                Placer(support, m_Supports[s], p.positionRecap, echelle * echelleRecap);
                var rendu = m_RecapsRendus[s][k];
                rendu.sprite = Symbole(p.cases[0].solution);
                rendu.enabled = rendu.sprite != null;
            }
        }
    }

    /// <summary>Appelé par CaseSymbole quand on la poke : passe au symbole suivant.</summary>
    public void Avancer(Case c)
    {
        if (EstResolu || symboles.Length == 0 || !livre.EstOuvert || livre.EstOccupe)
            return;

        c.actuel = (c.actuel + 1) % symboles.Length; // après le dernier, on revient au premier
        if (sonPoke != null)
            sonPoke.Play();

        VerifierTout();
    }

    // Une page est bonne quand toutes ses cases ont la bonne réponse ET que son sort a déjà été lancé.
    bool PageEstBonne(PageSymboles p)
    {
        foreach (var c in p.cases)
            if (c.actuel != c.solution)
                return false;
        return SortDejaLance(p.nomSort);
    }

    // Recalcule l'état de chaque page à chaque changement (poke ou sort lancé) :
    // une page peut être validée, puis redevenir fausse si on change son symbole.
    void VerifierTout()
    {
        if (EstResolu)
            return;

        var tout = true;
        foreach (var p in pagesSymboles)
        {
            var bonne = PageEstBonne(p);
            var etaitValidee = m_PagesValidees.Contains(p);
            // [DBG] temporaire
            Debug.Log($"[DBG] page {p.page} ({p.nomSort}) : choisi={p.cases[0].actuel} attendu={p.cases[0].solution} sortLancé={SortDejaLance(p.nomSort)} -> bonne={bonne}", this);

            if (bonne && !etaitValidee)
            {
                m_PagesValidees.Add(p);
                if (sonPageValidee != null)
                    sonPageValidee.Play();
                Debug.Log($"SymbolesLivre : page {p.page} validée ({m_PagesValidees.Count}/{pagesSymboles.Length}).", this);
                onPageValidee.Invoke(p.page);
            }
            else if (!bonne && etaitValidee)
            {
                m_PagesValidees.Remove(p);
                Debug.Log($"SymbolesLivre : page {p.page} n'est plus validée ({m_PagesValidees.Count}/{pagesSymboles.Length}).", this);
                onPageInvalidee.Invoke(p.page);
            }

            if (!bonne)
                tout = false;
        }

        Debug.Log($"[DBG] VerifierTout : tout={tout}, sorts lancés = [{string.Join(", ", m_SortsLances)}], pageADebloquer={pageADebloquer}, nouveauContenu=\"{nouveauContenu}\"", this); // [DBG] temporaire
        if (!tout)
            return;

        // Les 5 sont bonnes en même temps : déblocage final (définitif, les cases se bloquent).
        EstResolu = true;
        if (sonResolu != null)
            sonResolu.Play();
        Debug.Log("SymbolesLivre : toutes les pages sont validées !", this);

        if (pageADebloquer >= 0 && !string.IsNullOrEmpty(nouveauContenu))
            livre.RemplacerPage(pageADebloquer, nouveauContenu);

        onToutResolu.Invoke();
    }

    // Place un objet sur sa feuille, au point "position" (0,0 = bas gauche, 1,1 = haut droite).
    void Placer(Transform t, Support feuille, Vector2 position, float taille)
    {
        var x = inverserGaucheDroite ? 1f - position.x : position.x;
        var y = inverserHautBas ? 1f - position.y : position.y;
        var normale = inverserCote ? -feuille.normale : feuille.normale;

        var pointLocal = feuille.min + x * feuille.axeLargeur + y * feuille.axeHauteur;
        var f = feuille.transform;

        // On décolle l'objet de quelques millimètres côté lisible, pour qu'il ne soit pas dans le papier.
        var normaleMonde = f.TransformDirection(normale).normalized;
        t.position = f.TransformPoint(pointLocal) + normaleMonde * decalage;

        // Orientation, comme si la racine du prefab était posée à plat sur la page :
        // son axe Y (vert) sort de la page, son axe Z (bleu) pointe vers le haut de la page.
        var hautMonde = f.TransformDirection(feuille.axeHauteur).normalized;
        t.rotation = Quaternion.LookRotation(hautMonde, normaleMonde) * Quaternion.Euler(rotationCase);

        // Pas de parent, donc l'échelle locale = la taille réelle : celle du prefab, multipliée par "taille".
        t.localScale = prefabCase.transform.localScale * taille;
    }

    // Index du symbole d'avant (sens = -1) ou d'après (sens = +1), en faisant le tour de la liste comme une roue.
    // Case vide : "après" = le premier symbole (celui qu'on aura au prochain poke), "avant" = le dernier.
    int Voisin(int index, int sens)
    {
        var n = symboles.Length;
        if (n == 0)
            return -1;
        if (index < 0)
            return sens > 0 ? 0 : n - 1;
        return (index + sens + n) % n;
    }

    Sprite Symbole(int index)
    {
        return index >= 0 && index < symboles.Length ? symboles[index] : null;
    }

    // --- Debug ----------------------------------------------------------------------------------

    [ContextMenu("Debug / Poke case page gauche")]
    void DebugPokeGauche() => PokePremiereCase(LivreVR.Surface.Gauche);

    [ContextMenu("Debug / Poke case page droite")]
    void DebugPokeDroite() => PokePremiereCase(LivreVR.Surface.Recto);

    [ContextMenu("Debug / Lancer tous les sorts")]
    void DebugLancerTousLesSorts()
    {
        foreach (var p in pagesSymboles)
            SortLance(p.nomSort);
    }

    [ContextMenu("Debug / Tout résoudre")]
    void DebugToutResoudre()
    {
        foreach (var p in pagesSymboles)
        {
            foreach (var c in p.cases)
                c.actuel = c.solution;
            SortLance(p.nomSort);
        }
        VerifierTout();
    }

    void PokePremiereCase(LivreVR.Surface surface)
    {
        if (m_ParPage.TryGetValue(livre.PageSur(surface), out var p) && p.cases.Length > 0)
            Avancer(p.cases[0]);
    }

    void LancerSortDeLaPage(LivreVR.Surface surface)
    {
        if (m_ParPage.TryGetValue(livre.PageSur(surface), out var p))
            SortLance(p.nomSort);
    }
}