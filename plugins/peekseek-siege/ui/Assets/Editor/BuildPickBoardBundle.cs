using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// Builds the Siege UI into the folder that gets uploaded to the Workshop.
///
/// This does what the U3-SDK's Master Bundle Tool does when you press Export, read off
/// EditorAssetBundleHelper.Build so a bundle from here is byte-for-byte the kind the
/// game expects: one BuildPipeline call per platform with the same two options, the
/// stray folder-named bundle deleted, and the .hash file the server reads written when
/// all three platforms are present. It exists so the whole SDK does not have to be
/// cloned to build a UI that uses none of it.
///
/// Run from the menu, or headless:
///
///   Unity.exe -batchmode -nographics -quit -projectPath <this folder>
///             -executeMethod BuildPickBoardBundle.Build -logFile build.log
public static class BuildPickBoardBundle {
    private const string BundleName = "peekseeksiege.masterbundle";
    private const string BundleRoot = "Assets/PeekSeekSiege/Bundles";
    private const string Prefab = BundleRoot + "/Effects/PickBoard/Effect.prefab";

    /// The mod folder. Everything in it goes to the Workshop as one item.
    private static readonly string Output = Path.GetFullPath("Build/PeekSeekSiegeUI");

    [MenuItem("PeekSeek/Build Siege UI bundle")]
    public static void Build() {
        AssetImporter prefab = AssetImporter.GetAtPath(Prefab);
        if (prefab == null) throw new FileNotFoundException("Missing " + Prefab);
        prefab.SetAssetBundleNameAndVariant(BundleName, "");

        Directory.CreateDirectory(Output);
        string[] assets = AssetDatabase.GetAssetPathsFromAssetBundle(BundleName);

        BuildAssetBundleOptions options =
            BuildAssetBundleOptions.DisableLoadAssetByFileName
            | BuildAssetBundleOptions.DisableLoadAssetByFileNameWithExtension;

        // Windows last, so the editor is left on the platform it started on.
        BuildFor(BuildTarget.StandaloneLinux64, "_linux", assets, options);
        BuildFor(BuildTarget.StandaloneOSX, "_mac", assets, options);
        BuildFor(BuildTarget.StandaloneWindows64, "", assets, options);

        RemoveBuildNoise();
        WriteHash();
        CopyDefinitions();

        Debug.Log("Siege UI bundle written to " + Output);
    }

    private static void BuildFor(BuildTarget target, string suffix, string[] assets, BuildAssetBundleOptions options) {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target)) {
            Debug.LogWarning($"{target} build support is not installed; skipping. Add it in Unity Hub "
                + "(Installs > Add modules) so the server and every player get a bundle for their platform.");
            return;
        }

        AssetBundleBuild build = new() {
            assetBundleName = NameFor(suffix),
            assetNames = assets
        };
        BuildPipeline.BuildAssetBundles(Output, new[] { build }, options, target);
    }

    /// `peekseeksiege_linux.masterbundle`: the suffix goes before the first dot, which
    /// is where the game looks for a platform's variant.
    private static string NameFor(string suffix) {
        int dot = BundleName.IndexOf('.');
        return BundleName.Insert(dot, suffix);
    }

    /// Unity writes a bundle named after the output folder plus a .manifest beside every
    /// bundle. Neither is part of a mod.
    private static void RemoveBuildNoise() {
        string folderBundle = Path.Combine(Output, Path.GetFileName(Output));
        if (File.Exists(folderBundle)) File.Delete(folderBundle);

        foreach (string manifest in Directory.GetFiles(Output, "*.manifest")) File.Delete(manifest);
    }

    /// The server reads one file holding all three platform hashes, version byte first.
    /// Without all three bundles there is nothing correct to write, so none is written.
    private static void WriteHash() {
        string windows = Path.Combine(Output, NameFor(""));
        string linux = Path.Combine(Output, NameFor("_linux"));
        string mac = Path.Combine(Output, NameFor("_mac"));
        string hashFile = windows + ".hash";

        if (!File.Exists(linux) || !File.Exists(mac)) {
            if (File.Exists(hashFile)) File.Delete(hashFile);
            Debug.LogWarning("Not every platform was built, so no .hash file was written.");
            return;
        }

        using SHA1 sha1 = SHA1.Create();
        byte[] hashes = new byte[61];
        hashes[0] = 2;
        sha1.ComputeHash(File.ReadAllBytes(windows)).CopyTo(hashes, 1);
        sha1.ComputeHash(File.ReadAllBytes(linux)).CopyTo(hashes, 21);
        sha1.ComputeHash(File.ReadAllBytes(mac)).CopyTo(hashes, 41);
        File.WriteAllBytes(hashFile, hashes);
    }

    /// The .dat files sit beside the prefab in the project so the folder shape the game
    /// walks is the same shape Unity built the paths from. The mod needs them too.
    private static void CopyDefinitions() {
        foreach (string source in Directory.GetFiles(BundleRoot, "*.dat", SearchOption.AllDirectories)) {
            string relative = source.Substring(BundleRoot.Length + 1);
            string target = Path.Combine(Output, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target, overwrite: true);
        }
    }
}
