using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeekRaid.Core.Models;
using Rocket.Core.Utils;
using SDG.Unturned;

namespace PeekSeekRaid.Core;

/// A team's building budget, and the inventories that stand in for it.
///
/// A builder never carries the kit. For every kind of piece the team still has they
/// carry one item whose `amount` is the count, so the housing planner reads "40+0" off
/// it, placing one takes it to 39 through the game's own DeleteAmount, and the planner
/// keeps the piece selected because it never left. Doors and hatches are placed by
/// hand, and equipment.use deletes the whole item and then equips the next of its
/// kind, so those are carried as two: one goes down, the other is already in hand,
/// and the sync puts a second back.
///
/// The budget is the ledger; the inventory is a view of it. A placement spends here
/// once the piece has spawned, a salvage refunds here from the salvage hook, and after
/// either the inventories are put back in step a frame later, so the inventory is never
/// edited from inside its own event.
internal class BuildBudget {
    internal Budget Counts { get; }

    /// Fired after anything changes what is left, so the panel can be redrawn.
    internal event System.Action Changed;

    /// The handlers hooked into one builder's inventory, kept so they can be unhooked
    /// again when the build phase ends or the builder leaves.
    private sealed class Watcher {
        internal Player Player;
        internal InventoryRemoved Removed;
        internal DropItemRequestHandler Drop;
    }

    private readonly Dictionary<ulong, Watcher> builders = [];
    private bool syncing;

    internal BuildBudget(IEnumerable<RaidItem> supplies) {
        Counts = new Budget(supplies);
    }

    /// Keep this player's inventory in step with the budget from now on. Calling it for
    /// a builder already watched puts their inventory back in step, which is what a
    /// builder who has just been re-dressed needs.
    internal void Watch(Player player) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;

        if (!builders.ContainsKey(steam64)) {
            Watcher watcher = new() {
                Player = player,
                Removed = (page, index, jar) => OnRemoved(player, jar),
                Drop = RefuseDrop
            };
            builders[steam64] = watcher;
            player.inventory.onInventoryRemoved += watcher.Removed;
            player.inventory.onDropItemRequested += watcher.Drop;
        }

        Sync(player);
    }

    internal void Unwatch(Player player) {
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        if (!builders.TryGetValue(steam64, out Watcher watcher)) return;

        builders.Remove(steam64);
        player.inventory.onInventoryRemoved -= watcher.Removed;
        player.inventory.onDropItemRequested -= watcher.Drop;
    }

    internal void UnwatchAll() {
        foreach (Watcher watcher in builders.Values.ToList()) Unwatch(watcher.Player);
    }

    internal bool IsWatching(Player player) => builders.ContainsKey(player.channel.owner.playerID.steamID.m_SteamID);

    /// One of `id` has gone down. The game deletes the copy from the placer's
    /// inventory itself; the sync a frame later brings everybody else's in line.
    /// Refused, and nothing changes, when there was none left.
    internal bool Spend(ushort id) {
        if (!Counts.Spend(id)) return false;

        Later(SyncAll);
        Changed?.Invoke();
        return true;
    }

    /// One of `id` is back on the pile. The game hands the salvager a fresh copy, which
    /// the sync a frame later folds into the one they already carry.
    internal void Refund(ushort id) {
        Counts.Refund(id);
        Later(SyncAll);
        Changed?.Invoke();
    }

    /// Make one player's inventory the budget's view: for each piece, one item carrying
    /// the count, or two items for a piece placed by hand, or none.
    internal void Sync(Player player) {
        syncing = true;
        try {
            foreach (ushort id in Counts.Ids) {
                List<(byte Page, byte Index)> carried = Carried(player, id);

                if (Assets.find(EAssetType.ITEM, id) is ItemBarricadeAsset) SyncByHand(player, id, carried);
                else SyncStack(player, id, carried);
            }
        } finally {
            syncing = false;
        }
    }

    /// A piece placed by hand: two in the bag while there are two to lay.
    private void SyncByHand(Player player, ushort id, List<(byte Page, byte Index)> carried) {
        int wanted = Math.Min(Counts.Left(id), 2);
        for (int i = carried.Count; i > wanted; i--) Remove(player, carried[i - 1]);
        for (int i = carried.Count; i < wanted; i++) Add(player, id, 1);
    }

    /// A piece the planner lays: one item whose amount is the count, and no others.
    private void SyncStack(Player player, ushort id, List<(byte Page, byte Index)> carried) {
        int left = Counts.Left(id);
        int keep = left == 0 ? 0 : 1;
        foreach ((byte Page, byte Index) extra in carried.Skip(keep).Reverse()) Remove(player, extra);
        if (left == 0) return;

        byte amount = (byte)Math.Min(left, byte.MaxValue);
        if (carried.Count == 0) {
            Add(player, id, amount);
            return;
        }

        ItemJar kept = player.inventory.getItem(carried[0].Page, carried[0].Index);
        if (kept.item.amount != amount) player.inventory.sendUpdateAmount(carried[0].Page, kept.x, kept.y, amount);
    }

    private void SyncAll() {
        foreach (Watcher watcher in builders.Values.ToList()) Sync(watcher.Player);
    }

    /// A budget piece leaving the inventory is the game consuming what it placed, or a
    /// player moving it around; either way the view is put back in step next frame.
    private void OnRemoved(Player player, ItemJar jar) {
        if (syncing || !IsWatching(player) || !Counts.Ids.Contains(jar.item.id)) return;

        Later(() => {
            if (IsWatching(player)) Sync(player);
        });
    }

    /// Dropped pieces would be free pieces: the drop leaves the inventory, the sync
    /// puts another in, and the floor fills up.
    private static void RefuseDrop(PlayerInventory inventory, Item item, ref bool shouldAllow) {
        shouldAllow = false;
    }

    private static void Later(System.Action work) => TaskDispatcher.QueueOnMainThread(work);

    private static void Add(Player player, ushort id, byte amount) {
        Item item = new(id, true) { amount = amount };
        player.inventory.tryAddItemAuto(item, false, false, false, false);
    }

    private static void Remove(Player player, (byte Page, byte Index) slot) {
        player.inventory.removeItem(slot.Page, slot.Index);
    }

    /// Every slot holding `id`, hands first, in inventory order.
    private static List<(byte Page, byte Index)> Carried(Player player, ushort id) {
        List<(byte, byte)> found = [];
        for (byte page = 0; page < PlayerInventory.STORAGE; page++) {
            Items items = player.inventory.items[page];
            if (items == null) continue;

            for (byte index = 0; index < items.getItemCount(); index++) {
                if (items.getItem(index).item.id == id) found.Add((page, index));
            }
        }
        return found;
    }
}
