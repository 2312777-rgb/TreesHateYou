#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class SpookyTreeImport
{
    private const string Folder = "Assets/Imported/SpookyTree";
    static SpookyTreeImport() { EditorApplication.delayCall += BuildPrefab; }

    [MenuItem("Tools/Spooky Tree/Build Prefab")]
    private static void BuildPrefab()
    {
        string prefabPath = Folder + "/Resources/Spooky Tree.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) return;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/source/Spooky Boom.fbx");
        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/textures/TheSadBoomTexture2.0.png");
        var emission = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/textures/DeSadBoomEmission.png");
        if (model == null || color == null || emission == null) return;

        Shader shader = Shader.Find("Standard");
        if (shader == null) return;
        string materialPath = Folder + "/Spooky Tree.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            material.mainTexture = color;
            material.SetTexture("_EmissionMap", emission);
            material.SetColor("_EmissionColor", new Color(15f, 8f, 25f));
            material.SetFloat("_Glossiness", 0.15f);
            material.EnableKeyword("_EMISSION");
            AssetDatabase.CreateAsset(material, materialPath);
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            instance.name = "Spooky Tree";
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int index = 0; index < slots.Length; index++) slots[index] = material;
                renderer.sharedMaterials = slots;
            }
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }
}
#endif
