using Steamworks;
using Steamworks.Ugc;

// Publishes a built mod or map to the Steam Workshop, or updates it there.
//
//   dotnet run -- <folder> --title "PeekSeek UI"           new item
//   dotnet run -- <folder> --update <file id>              update that item
//   dotnet run -- <folder> --type Map                      a map rather than a bundle
//   dotnet run -- <folder> --changes "text"                a change note, on any
//
// It talks to the Steam client that is already running and logged in on this machine,
// so it needs no password and no API key; there is no other way to publish, because
// Steam only takes Workshop uploads through a logged-in session.
//
// What it sets is what Unturned's own submit menu sets (TempSteamworksWorkshop.updateUGC
// in the U3-SDK), because that is how the game decides what a downloaded item is: a
// one-byte Object.meta or Map.meta in the folder, the matching tag, and for a bundle
// the key-value tag compatibility_version = 6, the master bundle version for 2022 LTS.
// Unlisted, so the server can hand the id to anybody who joins without the item turning
// up in search.

const uint Unturned = 304930;
const string CompatibilityVersion = "6";

string? folder = args.Length > 0 && !args[0].StartsWith("--") ? Path.GetFullPath(args[0]) : null;
ulong existing = Flag("--update") is string id ? ulong.Parse(id) : 0;
string changes = Flag("--changes") ?? (existing == 0 ? "First upload." : "Rebuilt.");
string type = Flag("--type") ?? "Object";
bool isMap = type.Equals("Map", StringComparison.OrdinalIgnoreCase);

if (folder == null || (!isMap && !File.Exists(Path.Combine(folder, "MasterBundle.dat")))) {
    Console.Error.WriteLine("Give the mod folder: the one holding MasterBundle.dat, usually ui/Build/<mod>; or a map folder with --type Map.");
    return 2;
}

string title = Flag("--title") ?? (isMap ? Path.GetFileName(Directory.GetDirectories(folder).First()) : "PeekSeek UI");
string description = Flag("--description") ?? (isMap
    ? File.ReadAllLines(Path.Combine(Directory.GetDirectories(folder).First(), "English.dat"))
        .FirstOrDefault(line => line.StartsWith("Description "))?.Substring("Description ".Length) ?? title
    : "The screens PeekSeek servers draw in game. Servers list this item so it downloads on join; there is nothing to do with it on its own.");

// The site's logo, found by walking up to the repo, so the item looks like PeekSeek's;
// a map has its own preview.
string preview = isMap
    ? Path.Combine(Directory.GetDirectories(folder).First(), "Preview.png")
    : Path.Combine(RepoRoot(), "website", "public", "img", "logo red.png");

// The marker the game reads to learn what kind of content this is. Unturned's uploader
// writes the same file into the folder it submits.
File.WriteAllBytes(Path.Combine(folder, isMap ? "Map.meta" : "Object.meta"), new byte[1]);

try {
    SteamClient.Init(Unturned, asyncCallbacks: true);
} catch (Exception ex) {
    Console.Error.WriteLine($"Steam is not available: {ex.Message}. Steam has to be running and logged in.");
    return 1;
}

try {
    Console.WriteLine($"Logged in as {SteamClient.Name} ({SteamClient.SteamId}).");

    Editor editor = (existing == 0 ? Editor.NewCommunityFile : new Editor(existing))
        .WithTitle(title)
        .WithDescription(description)
        .WithContent(new DirectoryInfo(folder))
        .WithTag(isMap ? "Map" : "Object")
        .WithChangeLog(changes);
    if (!isMap) editor = editor.AddKeyValueTag("compatibility_version", CompatibilityVersion);
    editor = Unlisted(editor);

    if (File.Exists(preview)) editor = editor.WithPreviewFile(preview);
    else Console.WriteLine($"No preview image at {preview}; Steam will show a placeholder.");

    Progress<float> progress = new(p => Console.Write($"\r{p * 100,5:0}%"));
    PublishResult result = await editor.SubmitAsync(progress);
    Console.WriteLine();

    if (!result.Success) {
        Console.Error.WriteLine($"Steam refused the upload: {result.Result}.");
        return 1;
    }

    Console.WriteLine($"Published file id: {result.FileId}");
    Console.WriteLine($"https://steamcommunity.com/sharedfiles/filedetails/?id={result.FileId}");

    if (result.NeedsWorkshopAgreement) {
        Console.WriteLine("Steam wants the Workshop legal agreement accepted before the item is visible: "
            + "https://steamcommunity.com/sharedfiles/workshoplegalagreement");
    }

    return 0;
} finally {
    SteamClient.Shutdown();
}

string? Flag(string name) {
    int at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

// Facepunch.Steamworks 2.3.3's builder stops at Private, but the field it would set and
// the enum value Steam accepts are both there, so this fills in the missing method.
static Editor Unlisted(Editor editor) {
    const int unlisted = 3; // Steam's k_ERemoteStoragePublishedFileVisibilityUnlisted.

    System.Reflection.FieldInfo visibility = typeof(Editor)
        .GetField("Visibility", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    Type enumType = Nullable.GetUnderlyingType(visibility.FieldType)!;

    object boxed = editor;
    visibility.SetValue(boxed, Enum.ToObject(enumType, unlisted));
    return (Editor)boxed;
}

static string RepoRoot() {
    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "website"))) dir = dir.Parent;
    return dir?.FullName ?? AppContext.BaseDirectory;
}
