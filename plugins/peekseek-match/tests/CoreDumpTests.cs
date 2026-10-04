using System.IO;
using PeekSeek.Core;
using Xunit;

namespace PeekSeek.Tests;

/// The sweep deletes files, so what it will and will not match is worth pinning. A
/// pattern that is slightly too broad here quietly eats a server's config or its save.
public class CoreDumpTests : System.IDisposable {
    private readonly string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private string Rocket => Path.Combine(root, "Rocket");
    private string PluginFolder => Path.Combine(Rocket, "Plugins", "PeekSeek");

    public CoreDumpTests() {
        Directory.CreateDirectory(PluginFolder);
    }

    public void Dispose() {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private void Write(string name) {
        File.WriteAllText(Path.Combine(Rocket, name), "x");
    }

    private bool Survives(string name) => File.Exists(Path.Combine(Rocket, name));

    [Fact]
    public void DumpsAreClearedFromTheRocketFolder() {
        Write("core.47");
        Write("core.1234");

        CoreDumps.Sweep(PluginFolder);

        Assert.False(Survives("core.47"));
        Assert.False(Survives("core.1234"));
    }

    /// Everything Rocket actually keeps in that folder, which must all still be there
    /// afterwards. `core.log` is the trap: it starts with the right five characters.
    [Fact]
    public void NothingElseInTheFolderIsTouched() {
        Write("Rocket.config.xml");
        Write("Permissions.config.xml");
        Write("core.log");
        Write("core");
        Write("core.47.keep");
        Write("mycore.47");

        CoreDumps.Sweep(PluginFolder);

        Assert.True(Survives("Rocket.config.xml"));
        Assert.True(Survives("Permissions.config.xml"));
        Assert.True(Survives("core.log"));
        Assert.True(Survives("core"));
        Assert.True(Survives("core.47.keep"));
        Assert.True(Survives("mycore.47"));
    }

    /// The lobby keeps its DLL directly in Plugins rather than a folder of its own, so
    /// the walk up has to find Rocket from either depth.
    [Fact]
    public void TheRocketFolderIsFoundFromEitherPluginLayout() {
        Write("core.99");

        CoreDumps.Sweep(Path.Combine(Rocket, "Plugins"));

        Assert.False(Survives("core.99"));
    }

    /// A path with no Rocket folder above it means the layout is not what this expects,
    /// and deleting on a guess is worse than doing nothing.
    [Fact]
    public void NothingHappensWhenThereIsNoRocketFolderAbove() {
        string stray = Path.Combine(root, "Elsewhere");
        Directory.CreateDirectory(stray);
        File.WriteAllText(Path.Combine(stray, "core.47"), "x");

        CoreDumps.Sweep(stray);

        Assert.True(File.Exists(Path.Combine(stray, "core.47")));
    }

    [Fact]
    public void AMissingFolderIsNotAnError() {
        CoreDumps.Sweep(Path.Combine(root, "does", "not", "exist"));
    }
}
