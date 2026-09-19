using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeekSiege.Core.Models;
using Rocket.Core.Utils;
using SDG.Unturned;

namespace PeekSeekSiege.Core;

/// A team's building budget, and the inventories that stand in for it.
///
/// A builder never carries the kit. They carry exactly one of every piece the team
/// still has, so the housing planner lists everything that can still go down and
/// nothing that cannot, and the counts live here and on the panel beside the screen.
/// Placing a piece spends one from the budget; the game then deletes the copy from
/// the inventory, and this puts another back if there is still one to put. Salvaging
/// a piece hands the game's copy back to the inventory, and this takes it away again
/// and counts it as a refund. The inventory is a view of the budget, never the other
/// way round.
///
/// The syncing goes through the inventory's own added and removed events rather than
/// a timer, so the planner is never a second behind, and it is deferred a frame so
/// the inventory is never edited from inside its own event.
internal class BuildBudget {
    internal Budget Counts { get; }

    /// Fired after anything changes what is left, so the panel can be redrawn.
    internal event System.Action Changed;

    /// The handlers hooked into one builder's inventory, kept so they can be unhooked
    /// again when the build phase ends or the builder leaves.
    private sealed class Watcher {
        internal Player Player;
        internal InventoryAdded Added;
        internal InventoryRemoved Removed;
        internal DropItemRequestHandler Drop;
    }

    private readonly Dictionary<ulong, Watcher> builders = [];
    private bool syncing;

    internal BuildBudget(IEnumerable<SiegeItem> supplies) {
        Counts = new Budget(supplies);
    }

    /// Start keeping this player's inventory in step with the budget.
    internal void Watch(Player player) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        if (builders.ContainsKey(steam64)) return;

        Watcher watcher = new() {
            Player = player,
            Added = (page, index, jar) => OnAdded(player, jar),
            Removed = (page, index, jar) => OnRemoved(player, jar),
            Drop = RefuseDrop
        };
        builders[steam64] = watcher;
        player.inventory.onInventoryAdded += watcher.Added;
        player.inventory.onInventoryRemoved += watcher.Removed;
        player.inventory.onDropItemRequested += watcher.Drop;
        Sync(player);
    }

    internal void Unwatch(Player player) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        if (!builders.TryGetValue(steam64, out Watcher watcher)) return;

        builders.Remove(steam64);
        player.inventory.onInventoryAdded -= watcher.Added;
        player.inventory.onInventoryRemoved -= watcher.Removed;
        player.inventory.onDropItemRequested -= watcher.Drop;
    }

    internal void UnwatchAll() {
        foreach (Watcher watcher in builders.Values.ToList()) Unwatch(watcher.Player);
    }

    internal bool IsWatching(Player player) => builders.ContainsKey(player.channel.owner.playerID.steamID.m_SteamID);

    /// Take one of `id` for a placement. Refused when there is none left, which the
    /// deploy gate turns into a refused placement.
    internal bool Spend(ushort id) {
        if (!Counts.Spend(id)) return false;

        Changed?.Invoke();
        return true;
    }

    internal void Refund(ushort id) {
        Counts.Refund(id);
        SyncAll();
        Changed?.Invoke();
    }

    /// Make one player's inventory exactly one of every piece with something left.
    internal void Sync(Player player) {
        syncing = true;
        try {
            foreach (ushort id in Counts.Ids) {
                int carried = Carried(player, id);
                int wanted = Counts.Has(id) ? 1 : 0;

                for (; carried > wanted; carried--) RemoveOne(player, id);
                for (; carried < wanted; carried++) {
                    player.inventory.tryAddItemAuto(new Item(id, true), false, false, false, false);
                }
            }
        } finally {
            syncing = false;
        }
    }

    private void SyncAll() {
        foreach (Watcher watcher in builders.Values) Sync(watcher.Player);
    }

    /// A budget piece arriving from anywhere but here is one the game handed back, a
    /// salvage or a pickup, so it counts as a refund and the copy goes.
    private void OnAdded(Player player, ItemJar jar) {
        if (syncing || !IsWatching(player) || !IsBudgetItem(jar.item.id)) return;

        TaskDispatcher.QueueOnMainThread(() => {
            if (!IsWatching(player)) return;
            Counts.Refund(jar.item.id);
            Sync(player);
            Changed?.Invoke();
        });
    }

    /// A budget piece leaving the inventory is the game consuming the one it placed,
    /// after the deploy gate already spent it here; put the next one in their hands.
    private void OnRemoved(Player player, ItemJar jar) {
        if (syncing || !IsWatching(player) || !IsBudgetItem(jar.item.id)) return;

        TaskDispatcher.QueueOnMainThread(() => {
            if (IsWatching(player)) Sync(player);
        });
    }

    /// Dropped pieces would be free pieces: the drop leaves the inventory, the sync
    /// puts another in, and the floor fills up.
    private static void RefuseDrop(PlayerInventory inventory, Item item, ref bool shouldAllow) {
        shouldAllow = false;
    }

    private bool IsBudgetItem(ushort id) => Counts.Ids.Contains(id);

    private static int Carried(Player player, ushort id) {
        int count = 0;
        for (byte page = 0; page < PlayerInventory.STORAGE; page++) {
            Items items = player.inventory.items[page];
            if (items == null) continue;

            for (byte index = 0; index < items.getItemCount(); index++) {
                if (items.getItem(index).item.id == id) count++;
            }
        }
        return count;
    }

    private static void RemoveOne(Player player, ushort id) {
        for (byte page = 0; page < PlayerInventory.STORAGE; page++) {
            Items items = player.inventory.items[page];
            if (items == null) continue;

            for (byte index = 0; index < items.getItemCount(); index++) {
                if (items.getItem(index).item.id != id) continue;

                player.inventory.removeItem(page, index);
                return;
            }
        }
    }
}
