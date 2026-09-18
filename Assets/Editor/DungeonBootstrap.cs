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
    // Sliced by KenneyCharacterSlicer.SlicePlayerSprite (Multiple sprite mode, named sub-rects) -
    // baked into the same IconPackData/lookup as the icon atlas so DungeonGenerator.LoadIconPackSprite
    // works for both without a second runtime code path.
    const string CharacterAtlasPath = "Assets/Kenney Game Assets/2D assets/Roguelike Characters Pack/Spritesheet/roguelikeChar_transparent.png";
    // Sliced by KenneyDungeonTileSlicer.SliceDungeonTiles - floor/wall tiles sampled pixel-by-pixel
    // at runtime (DungeonGenerator.CreateTexturedFloorSprite/CreateWallSprite), not just assigned
    // whole to a SpriteRenderer like the two atlases above.
    const string DungeonTileAtlasPath = "Assets/Kenney Game Assets/2D assets/Roguelike Dungeon Pack/Spritesheet/roguelikeDungeon_transparent.png";

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

    [MenuItem("Dungeon/Generate Training Room")]
    public static void BuildTrainingRoom()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

        DungeonGenerator.BuildTrainingRoom(Random.Range(int.MinValue, int.MaxValue));

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
        foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(CharacterAtlasPath))
        {
            if (obj is Sprite sprite) data.entries.Add(new IconPackData.Entry { name = sprite.name, sprite = sprite });
        }
        foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(DungeonTileAtlasPath))
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
