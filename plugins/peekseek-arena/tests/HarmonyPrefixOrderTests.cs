using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Xunit;

namespace PeekSeekArena.Tests;

/// The regression that stopped every arena match from starting, pinned.
///
/// Match patches four LevelManager methods with prefixes that return false, switching the
/// game's native Arena loop off for every gamemode. ARENA patches the same four with what
/// it wants them to do instead. Harmony runs every prefix on a method but skips the rest
/// as soon as one returns false, so if Match's do-nothing prefix is ordered first,
/// ARENA's never runs at all - which is exactly what happened until ARENA 1.1.3. Nothing
/// in CI caught it, and the symptom was silence: no error, no arena, nothing.
///
/// The only thing keeping the two apart is that ARENA's prefixes carry
/// [HarmonyPriority(Priority.First)]. That is the contract, and this asserts it.
///
/// It reads the attribute out of the built assembly's metadata rather than running
/// Harmony, because patching the real LevelManager needs the game loaded. Cecil rather
/// than reflection, because reflecting over a patch class resolves its Unturned parameter
/// types, and one of the game's redistributed assemblies targets netstandard2.1, which
/// net48 cannot load. Cecil reads the metadata and resolves nothing, so this test needs
/// no game assemblies at all.
public class HarmonyPrefixOrderTests {
    /// The LevelManager methods Match also prefixes with a do-nothing that returns false.
    /// A prefix of ours on any of these has to run before Match's.
    private static readonly string[] ContestedMethods =
        ["arenaLobby", "arenaPlay", "arenaSpawn", "findGroups"];

    private const int PriorityFirst = 800; // HarmonyLib.Priority.First

    /// A plain class rather than a record: records need System.Runtime.CompilerServices
    /// .IsExternalInit, which net48 does not ship.
    private sealed class Patch {
        public string Class;
        public string Method;
        public int? Priority;
        public string ReturnType;
    }

    /// Every Harmony patch class in the built plugin, with what its Prefix targets and how
    /// it is ordered. The target method name is the second argument of a [HarmonyPatch]
    /// attribute, which may be on the same attribute as the type or on one of its own -
    /// both spellings are in use here, so every string argument is considered.
    private static List<Patch> PluginPatches() {
        var path = typeof(PeekSeekArenaConfiguration).Assembly.Location;
        // The test assembly sits beside the plugin it references.
        var dll = Path.Combine(Path.GetDirectoryName(path)!, "PeekSeekArena.dll");
        Assert.True(File.Exists(dll), $"built plugin not found at {dll}");

        using var assembly = AssemblyDefinition.ReadAssembly(dll);
        var patches = new List<Patch>();

        foreach (var type in assembly.MainModule.Types) {
            var targets = type.CustomAttributes
                .Where(a => a.AttributeType.Name == "HarmonyPatch")
                .SelectMany(a => a.ConstructorArguments)
                .Where(argument => argument.Type.FullName == "System.String")
                .Select(argument => (string)argument.Value)
                .ToArray();
            if (targets.Length == 0) continue;

            var prefix = type.Methods.FirstOrDefault(m => m.Name == "Prefix");
            if (prefix is null) continue;

            var priority = prefix.CustomAttributes
                .Where(a => a.AttributeType.Name == "HarmonyPriority")
                .SelectMany(a => a.ConstructorArguments)
                .Select(argument => (int?)(int)argument.Value)
                .FirstOrDefault();

            foreach (var target in targets)
                patches.Add(new Patch {
                    Class = type.Name,
                    Method = target,
                    Priority = priority,
                    ReturnType = prefix.ReturnType.FullName,
                });
        }

        return patches;
    }

    [Fact]
    public void EveryPrefixOnAMethodMatchAlsoPatches_RunsFirst() {
        var contested = PluginPatches()
            .Where(patch => ContestedMethods.Contains(patch.Method))
            .ToArray();

        // Guards the list: if a patch is renamed or Match starts switching off a fifth
        // method, the assertions below would quietly cover less than they claim to.
        Assert.Equal(
            ContestedMethods.OrderBy(name => name),
            contested.Select(patch => patch.Method).OrderBy(name => name)
        );

        foreach (var patch in contested) {
            Assert.True(
                patch.Priority == PriorityFirst,
                $"{patch.Class}.Prefix patches {patch.Method}, which Match also prefixes with a "
                    + "do-nothing that returns false. Without [HarmonyPriority(Priority.First)] "
                    + "Harmony may run Match's first and skip this one entirely, and the arena "
                    + $"silently never starts. Priority was {patch.Priority?.ToString() ?? "unset"}."
            );

            // The other half of the same decision: a prefix that returns void runs beside
            // the original instead of replacing it, and the ordering would not matter.
            Assert.Equal("System.Boolean", patch.ReturnType);
        }
    }
}
