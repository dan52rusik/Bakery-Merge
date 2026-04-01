#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class WebBuildProfileSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string BuildFolder = "Builds/WebGL";

    [MenuItem("Tools/OverHeat/Setup Web Build Profile")]
    public static void SetupWebBuildProfile()
    {
        if (!System.IO.File.Exists(ScenePath))
        {
            EditorUtility.DisplayDialog("Web Build Profile", $"Scene not found:\n{ScenePath}", "OK");
            return;
        }

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };

        PlayerSettings.companyName = "OverHeat Games";
        PlayerSettings.productName = "Bakery Merge";
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.runInBackground = true;

        PlayerSettings.WebGL.template = "PROJECT:YandexGames";
        PlayerSettings.WebGL.nameFilesAsHashes = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.memorySize = 64;

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog(
            "Web Build Profile",
            $"WebGL settings applied.\n\nBuild output folder:\n{BuildFolder}",
            "OK");
    }

    [MenuItem("Tools/OverHeat/Open Build Profiles")]
    public static void OpenBuildProfiles()
    {
        var windowType = Type.GetType("UnityEditor.Build.Profile.BuildProfileWindow, UnityEditor.BuildProfileModule");
        if (windowType != null)
        {
            EditorWindow.GetWindow(windowType);
            return;
        }

        EditorUtility.DisplayDialog("Build Profiles", "Build Profiles window type was not found.", "OK");
    }

    [MenuItem("Tools/OverHeat/Open Web Build Folder")]
    public static void OpenWebBuildFolder()
    {
        var fullPath = EnsureWebBuildFolder();
        EditorUtility.RevealInFinder(fullPath);
    }

    [MenuItem("Tools/OverHeat/Build WebGL Release")]
    public static void BuildWebGlRelease()
    {
        SetupWebBuildProfile();

        var fullPath = EnsureWebBuildFolder();
        var buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            target = BuildTarget.WebGL,
            locationPathName = fullPath,
            options = BuildOptions.None
        };

        BuildPipeline.BuildPlayer(buildPlayerOptions);
    }

    private static string EnsureWebBuildFolder()
    {
        var fullPath = System.IO.Path.GetFullPath(BuildFolder);
        System.IO.Directory.CreateDirectory(fullPath);
        return fullPath;
    }
}
#endif
