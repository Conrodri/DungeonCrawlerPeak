using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Thin Editor-only wrapper around DungeonGenerator (Assets/Scripts/DungeonGenerator.cs, runtime-
// safe) - only the parts that must stay in the Editor live here: the menu item, opening/saving the
// scene file, and baking the icon pack lookup Resources needs at runtime.
public static class DungeonBootstrap
{
    const string IconPackAtlasPath = "Assets/Modern GDR - Free icons pack/00_Atlas/BrightIcons.png";

    [MenuItem("Dungeon/Generate Floor")]
    public static void Build()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

        DungeonGenerator.Build(Random.Range(int.MinValue, int.MaxValue));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Dungeon/Generate Tutorial Room")]
    public static void BuildTutorial()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

        DungeonGenerator.BuildTutorial(Random.Range(int.MinValue, int.MaxValue));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    // Only needs re-running if the icon pack atlas itself changes - the resulting asset is a
    // normal committed project asset, not regenerated on every floor.
    [MenuItem("Dungeon/Rebuild Icon Pack Data")]
    public static void RebuildIconPackData()
    {
        var data = ScriptableObject.CreateInstance<IconPackData>();
        foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(IconPackAtlasPath))
        {
            if (obj is Sprite sprite) data.entries.Add(new IconPackData.Entry { name = sprite.name, sprite = sprite });
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");

        const string path = "Assets/Resources/IconPackData.asset";
        if (AssetDatabase.LoadAssetAtPath<IconPackData>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("DungeonBootstrap: baked " + data.entries.Count + " icon pack sprites into " + path);
    }
}
