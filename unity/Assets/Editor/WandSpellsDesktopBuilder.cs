using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

static class WandSpellsDesktopBuilder {
    const string ScenePath = "Assets/Scenes/WandSpellsMeetingDemo.unity";

    [MenuItem("Tools/Wand Spells/Build Desktop Player/Windows x64")]
    static void BuildWindows() {
        Build(BuildTarget.StandaloneWindows64, "Windows", "WandSpells.exe");
    }

    [MenuItem("Tools/Wand Spells/Build Desktop Player/macOS")]
    static void BuildMac() {
        Build(BuildTarget.StandaloneOSX, "Mac", "WandSpells.app");
    }

    static void Build(BuildTarget target, string platformFolder, string playerName) {
        var group = BuildTargetGroup.Standalone;
        if (!BuildPipeline.IsBuildTargetSupported(group, target)) {
            EditorUtility.DisplayDialog("Build module missing",
                "Install the " + target + " Build Support module in Unity Hub, then try again.", "OK");
            return;
        }
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target)) {
            EditorUtility.DisplayDialog("Could not switch build target",
                "Unity could not switch to " + target + ".", "OK");
            return;
        }
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds",
            "DesktopPlayer", platformFolder, playerName));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = target,
            options = BuildOptions.None
        });
        if (report.summary.result == BuildResult.Succeeded) {
            EditorUtility.DisplayDialog("Desktop player built",
                "Player saved to:\n" + output + "\n\nNext, build the one-file launcher with desktop_bundle.spec.", "OK");
            EditorUtility.RevealInFinder(Path.GetDirectoryName(output));
        } else {
            Debug.LogError("Desktop player build failed: " + report.summary.result);
            EditorUtility.DisplayDialog("Desktop player build failed",
                "See the Unity Console for details.", "OK");
        }
    }
}
