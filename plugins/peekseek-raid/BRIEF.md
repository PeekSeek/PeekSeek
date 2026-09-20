# The brief

The original instructions for this gamemode, saved verbatim so the reasoning behind the
rules is not lost when the code moves on. Where the implementation deviates, the
deviation and the reason for it are noted under "Deviations" at the bottom — the brief
itself is not edited.

---

> branch out, we're going to work on a new gamemode
> - to start this, let's work on an mvp with just one plugin, pick 1 fanghosting eu server and remove the peekseek plugins so that you just have this new one for testing
>
> the gamemode will be inspired by rainbow 6, instead of bombsite bla bla bla you will
>
> use locations
> - (74.97, 65.65, 311.35):8.062576, 321.4624
> - (234.63, 52.23, -240.41):358.9875, 354.825
>
> so how this will work,
> - 2 teams, each team first gets 5min to build their "base", both teams are on the same map so make sure they dont get to walk that far away from their base as it would let players peek at the other's base
> - you will give them a large inventory (adjust their hand or backpack size) and give them many building supplies
> - after 5min you save their base
> - 3 rounds defence, 3 rounds offense, tie breakers are 2 rounds defence, 2 rounds offense until infinity
> - in a round (make a unity UI, look up how to do this properly for unturned) you will present each time with 5 options, 1 for defence traps, the other for offensive raiding tools, each round they will get to pick what they want to use to raid the other team or try to defend their base (they can no longer make changes to the base itself)
> - this will be on an alpha valley arena map, so assure you look at the source code @ https://github.com/SmartlyDressedGames/U3-SDK and place the arena around the map properly
> - defenders have to be standing on a structure or they will take damage, the same as arena damage, only make them take damage after 2 seconds
> - when starting to build their place, place a metal floor id 369 with a claimflag on it on the middle id 1158, make sure u dont assign these to the group at all, IMPORTANT, otherwise they can salvage and move them, do not give the users floor's either, this makes it so that they cannot make 2 different bases and have to build off of that metal floor
> - dont give defenders dmg in build phase obviously, for doors give them only metal doors and metal hatches only and make it so these metal doors are assigned to the group everytime you load it in, for the rest only assign birch walls to the group itself when they load in outside the build phase, so that they can salvage these "soft" walls creating new openings, this shouldnt be the case for other walls, floors or pillars
>
> since there are no other plugins yet, no control plugins either, this means that i will need a /start command and allow me to test the whole gamemode solo, figure out a way to either let me be both teams or smth else, you can figure that out ur smart enough
>
> save these instructions on the branch as well as not crediting yourself in commits, also save a human explanation, for the code itself assume PeekSeek's main match plugin will later have hooks somehow for gamemodes, so make sure u dont waste ur time here fully and make it already hookable basically, still get player kits from the peekseek api for now, just use meta kit and default items from that gamemode thats fine, UI HAS to work and focus a bit on the UX make it feel peekseek

Follow-up, mid-build:

> wait for the match to finnish on eu10 btw

> rename it debug then

---

## Deviations

Each with a reason.

**The Unity UI needs a workshop bundle, which cannot be built from code.** Unturned has
exactly one way to draw a custom UI: `EffectManager.SendUIEffect`, which takes an
`EffectAsset`. An `EffectAsset` only exists inside a Unity asset bundle that the client
has already downloaded, so a UI cannot ship inside a `.dll`. The board is therefore
written against both: give it an effect GUID in config and it draws the real Unity
layout, leave the GUID empty and it draws the same five options to chat and takes
`/pick`. `ui/README.md` is the spec for the prefab that GUID has to point at. The chat
form is not a stopgap to be deleted: a player still downloading the bundle has to be
able to answer the board too.

**The building supplies are a team budget, not an inventory.** A builder carries one of
each piece the team still has; the housing planner lists them, the panel beside the
screen counts them, and placing one takes it off the pile and puts the next in hand.
That is what "a large inventory" was for, without the mess of a hundred items on a
hands page, and it is what makes swapping a placed piece for another material a matter
of counting.

**The flag is not a claim flag.** The claim flag's 32m bubble stops anybody outside
the owning group placing anything within it, on the client, which is exactly the
demolition charge an attacker needs to stick on a wall. The marker is the PeekSeek
Flag instead, the claim flag's model with 600 health and no bubble, placed by one
defender before every round rather than by the gamemode; the starting floors are
protected by where they stand, not by what they are. The map is vanilla Alpha Valley
as asked; Unturned's own arena boxes at the top of the screen cannot be switched off
from a server, so the HUD block is drawn exactly over them.

**The sentry is our own item.** A vanilla sentry only runs within reach of a fuelled
generator, and the server and every client each decide that for themselves, so a
server that lies about it gets a turret that shoots but looks dead. The PeekSeek
Sentry is the Hostile Sentry with `Requires_Power false`, shipped in the UI workshop
item beside the flag.

**Attackers may place charges.** The brief's raid tools were carried and thrown. With no
claim bubble a demolition charge can go on a wall, so it does.

**The gamemode ships as PeekSeek RAID, on PeekSeek Match.** The brief asked for the
code to be "hookable" by a future match plugin; that plugin now has the hooks
(`PeekSeek.Gamemodes`), and Siege became the RAID gamemode plugin on them. `/start`
moved to Match with it, as `/start raid`, and the kits come from the match rather than
from a fetch of this plugin's own.
