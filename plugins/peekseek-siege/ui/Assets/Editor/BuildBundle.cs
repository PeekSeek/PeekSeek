using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// Builds PeekSeek's in-game screens into the folder that gets uploaded to the Workshop.
///
/// This does what the U3-SDK's Master Bundle Tool does when you press Export, read off
/// EditorAssetBundleHelper.Build so a bundle from here is byte-for-byte the kind the
/// game expects: one BuildPipeline call per platform with the same two options, the
/// stray folder-named bundle deleted, and the .hash file the server reads written when
/// all three platforms are present. It exists so the whole SDK does not have to be
/// cloned to build screens that use none of it.
///
/// It also makes the two Inter font assets the prefabs reference. TextMeshPro needs an
/// SDF font asset, which only the Editor can create from a .ttf, and the prefabs are
/// written outside Unity against fixed GUIDs, so the assets are made here and pinned to
/// those GUIDs. They are build output, not source: git ignores them.
///
/// Run from the menu, or headless:
///
///   Unity.exe -batchmode -nographics -quit -projectPath <this folder>
///             -executeMethod BuildBundle.Build -logFile build.log
public static class BuildBundle {
    private const string BundleName = "peekseek.masterbundle";
    private const string BundleRoot = "Assets/PeekSeek/Bundles";
    private const string FontsRoot = "Assets/PeekSeek/Fonts";

    /// The mod folder. Everything in it goes to the Workshop as one item.
    private static readonly string Output = Path.GetFullPath("Build/PeekSeekUI");

    [MenuItem("PeekSeek/Build UI bundle")]
    public static void Build() {
        ImportTextMeshProResources();

        // The GUIDs unityyaml.py writes into every text component.
        EnsureFont("Inter-Regular", "7d1a3c9e5b2f4a6c8e0d1f2a3b4c5d6e");
        EnsureFont("Inter-Bold", "8e2b4d0f6c3a5b7d9f1e2a3b4c5d6e7f");

        foreach (string prefab in Directory.GetFiles(BundleRoot, "Effect.prefab", SearchOption.AllDirectories)) {
            AssetImporter.GetAtPath(prefab.Replace('\\', '/')).SetAssetBundleNameAndVariant(BundleName, "");
        }

        Directory.CreateDirectory(Output);
        string[] assets = AssetDatabase.GetAssetPathsFromAssetBundle(BundleName);
        if (assets.Length == 0) throw new FileNotFoundException("No Effect.prefab under " + BundleRoot);

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

        Debug.Log("PeekSeek UI bundle written to " + Output);
    }

    /// TextMeshPro's shaders, without which a font asset cannot be made: CreateFontAsset
    /// does `new Material(Shader.Find("TextMeshPro/Mobile/Distance Field"))`, and that
    /// shader ships inside TMP's Essential Resources .unitypackage rather than the
    /// package itself. AssetDatabase.ImportPackage did nothing in batch mode, so the
    /// package is unpacked here: it is a gzipped tar of <hash>/asset, asset.meta and
    /// pathname triples, and only the Assets/TextMesh Pro part of it is wanted.
    private static void ImportTextMeshProResources() {
        if (Directory.Exists("Assets/TextMesh Pro/Shaders")) return;

        string package = Path.GetFullPath(Path.Combine(
            "Packages", "com.unity.textmeshpro", "Package Resources", "TMP Essential Resources.unitypackage"));
        if (!File.Exists(package)) throw new FileNotFoundException("TextMesh Pro essential resources not found at " + package);

        Dictionary<string, Dictionary<string, byte[]>> entries = new();
        using (FileStream file = File.OpenRead(package))
        using (GZipStream gzip = new(file, CompressionMode.Decompress))
        foreach ((string name, byte[] data) in Tar(gzip)) {
            string[] parts = name.TrimStart('.', '/').Split('/');
            if (parts.Length != 2) continue;
            if (!entries.TryGetValue(parts[0], out Dictionary<string, byte[]> entry)) entries[parts[0]] = entry = new();
            entry[parts[1]] = data;
        }

        int written = 0;
        foreach (Dictionary<string, byte[]> entry in entries.Values) {
            if (!entry.TryGetValue("pathname", out byte[] pathBytes)) continue;
            string path = Encoding.UTF8.GetString(pathBytes).Split('\n')[0].Trim();
            if (!path.StartsWith("Assets/TextMesh Pro")) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (entry.TryGetValue("asset", out byte[] asset)) File.WriteAllBytes(path, asset);
            if (entry.TryGetValue("asset.meta", out byte[] meta)) File.WriteAllBytes(path + ".meta", meta);
            written++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"Unpacked {written} TextMesh Pro essential resources.");
    }

    /// The entries of a tar stream, ustar prefixes and GNU long names included, which
    /// is all a .unitypackage uses.
    private static IEnumerable<(string, byte[])> Tar(Stream stream) {
        byte[] header = new byte[512];
        string longName = null;

        while (Fill(stream, header) && header[0] != 0) {
            string name = Field(header, 0, 100);
            string prefix = Field(header, 345, 155);
            if (prefix.Length > 0) name = prefix + "/" + name;
            long size = System.Convert.ToInt64(Field(header, 124, 12).Trim('\0', ' '), 8);
            char type = (char)header[156];

            byte[] data = new byte[size];
            Fill(stream, data);
            long padding = (512 - size % 512) % 512;
            if (padding > 0) Fill(stream, new byte[padding]);

            if (type == 'L') {
                longName = Encoding.UTF8.GetString(data).TrimEnd('\0');
                continue;
            }

            if (longName != null) {
                name = longName;
                longName = null;
            }

            if (type == '0' || type == '\0') yield return (name, data);
        }
    }

    private static bool Fill(Stream stream, byte[] buffer) {
        int read = 0;
        while (read < buffer.Length) {
            int n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    private static string Field(byte[] header, int offset, int length) {
        int end = System.Array.IndexOf(header, (byte)0, offset, length);
        if (end < 0) end = offset + length;
        return Encoding.UTF8.GetString(header, offset, end - offset);
    }

    /// A dynamic SDF font asset from the .ttf of the same name, under a fixed GUID.
    ///
    /// Dynamic rather than baked so a player name in any script renders: glyphs are
    /// added at runtime from the .ttf, which the bundle carries because the asset
    /// references it. The GUID is pinned by rewriting the .meta Unity just wrote, which
    /// Unity honours on the next import.
    private static void EnsureFont(string name, string guid) {
        string assetPath = $"{FontsRoot}/{name} SDF.asset";
        if (AssetDatabase.AssetPathToGUID(assetPath) == guid) return;

        Font ttf = AssetDatabase.LoadAssetAtPath<Font>($"{FontsRoot}/{name}.ttf");
        if (ttf == null) throw new FileNotFoundException($"Missing {FontsRoot}/{name}.ttf");

        AssetDatabase.DeleteAsset(assetPath);
        TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(
            ttf, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        font.name = name + " SDF";
        AssetDatabase.CreateAsset(font, assetPath);

        font.material.name = font.name + " Material";
        AssetDatabase.AddObjectToAsset(font.material, font);
        font.atlasTexture.name = font.name + " Atlas";
        AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
        AssetDatabase.SaveAssets();

        string meta = assetPath + ".meta";
        File.WriteAllText(meta, Regex.Replace(File.ReadAllText(meta), "guid: [0-9a-f]{32}", "guid: " + guid));
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        if (AssetDatabase.AssetPathToGUID(assetPath) != guid) {
            throw new IOException($"{assetPath} did not take GUID {guid}; the prefabs' text will fall back to the game's font.");
        }
        Debug.Log($"Made {assetPath}.");
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

    /// `peekseek_linux.masterbundle`: the suffix goes before the first dot, which is
    /// where the game looks for a platform's variant.
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

    /// The .dat files sit beside the prefabs in the project so the folder shape the game
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
