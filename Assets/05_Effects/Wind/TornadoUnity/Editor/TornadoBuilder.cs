using UnityEngine;
using UnityEditor;
using System.IO;

// Recrée la "tornado Blue" du projet Godot sous forme de prefab Unity.
// Menu : Tools > Tornade > Créer le prefab
public static class TornadoBuilder
{
    // Réglages d'une couche, recopiés depuis node_3d.tscn
    struct Layer
    {
        public string name, noise;
        public Color color;
        public float intensity, threshold, speed, wobble, wobbleSpeed, gradMid, gradEnd;
        public Vector3 scale;
        public bool dome;
    }

    [MenuItem("Tools/Tornade/Créer le prefab")]
    public static void Build()
    {
        Shader shader = Shader.Find("Custom/GodotTornado");
        if (shader == null) { Debug.LogError("Shader Custom/GodotTornado introuvable."); return; }
        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(shader)).Replace('\\', '/');

        Mesh cone = FindObjMesh("tornado2");
        if (cone == null) { Debug.LogError("Mesh tornado2.obj introuvable dans le projet."); return; }
        Mesh sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");

        Layer[] layers =
        {
            new Layer { name = "Bleu",    noise = "A", color = new Color(0.403f, 0.554f, 0.885f), intensity = 15, threshold = 0.39f,  speed = -0.5f, wobble = 0.5f, wobbleSpeed = 1, gradMid = 0.533f, gradEnd = 0, scale = new Vector3(1.5f, 1.163f, 1.65f) },
            new Layer { name = "Blanc",   noise = "B", color = new Color(0.93f, 0.93f, 0.93f),    intensity = 5,  threshold = 0.817f, speed = -0.5f, wobble = 0.5f, wobbleSpeed = 1, gradMid = 0.533f, gradEnd = 0, scale = new Vector3(2.5f, 1.5f, 2.3f) },
            new Layer { name = "Sombre",  noise = "C", color = new Color(0.098f, 0.121f, 0.227f), intensity = 30, threshold = 0.538f, speed = -0.5f, wobble = 0.2f, wobbleSpeed = 1, gradMid = 1f,     gradEnd = 1, scale = new Vector3(2f, 1.25f, 2f) },
            new Layer { name = "Sol",     noise = "D", color = Color.white,                       intensity = 5,  threshold = 0.868f, speed = 0.5f,  wobble = 0f,   wobbleSpeed = 0, gradMid = 1f,     gradEnd = 1, scale = new Vector3(8f, 0.1f, 8f), dome = true },
        };

        GameObject root = new GameObject("Tornado_Wind");
        foreach (Layer l in layers)
        {
            Material mat = new Material(shader);
            mat.SetColor("_Color", l.color);
            mat.SetFloat("_Intensity", l.intensity);
            mat.SetTexture("_NoiseTex", LoadNoise(folder, l.noise));
            mat.SetFloat("_Threshold", l.threshold);
            mat.SetFloat("_Speed", l.speed);
            mat.SetVector("_Scale", new Vector4(2, 2, 0, 0));
            mat.SetVector("_PolarCenter", new Vector4(0.5f, 5f, 0, 0));
            mat.SetFloat("_Wobble", l.wobble);
            mat.SetFloat("_WobbleSpeed", l.wobbleSpeed);
            mat.SetFloat("_GradMid", l.gradMid);
            mat.SetFloat("_GradEnd", l.gradEnd);
            string matPath = $"{folder}/Tornado_{l.name}.mat";
            AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.CreateAsset(mat, matPath);

            GameObject go = new GameObject(l.name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = l.dome ? sphere : cone;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = l.scale;
            // Dans Godot, la base des cônes est 4.9 m au-dessus du disque au sol : on garde ce décalage
            // pour que l'origine du prefab soit au sol.
            go.transform.localPosition = l.dome ? Vector3.zero : new Vector3(-0.52f, 4.9f, 0.63f);
            go.transform.localRotation = l.dome ? Quaternion.identity : Quaternion.Euler(0, 71.8f, 0);
        }

        // Le modèle Godot fait ~13 m de haut : on réduit à ~40 cm
        root.transform.localScale = Vector3.one * 0.03f;

        string prefabPath = $"{folder}/Tornado_Wind.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("Prefab créé : " + prefabPath);
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
    }

    static Mesh FindObjMesh(string name)
    {
        // 1. Si tu as sélectionné le modèle dans la fenêtre Project, on prend celui-là
        if (Selection.activeObject != null)
        {
            string selPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(selPath))
                if (o is Mesh m) return m;
        }

        // 2. Sinon on parcourt tous les modèles 3D du projet et on compare le nom du fichier
        foreach (string guid in AssetDatabase.FindAssets("t:Model"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path).ToLower() != name.ToLower()) continue;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Mesh m) return m;
        }
        return null;
    }

    static Texture2D LoadNoise(string folder, string letter)
    {
        string path = $"{folder}/TornadoNoise_{letter}.png";
        // Réglages d'import : répétition activée, pas de correction sRGB (ce sont des données, pas des couleurs)
        TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null && (imp.sRGBTexture || imp.wrapMode != TextureWrapMode.Repeat))
        {
            imp.sRGBTexture = false;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
