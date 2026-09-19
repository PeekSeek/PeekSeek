# PeekSeek Kill Feed

A kill feed at the top right of every player's screen: who killed whom with what. The
weapon is a white silhouette, a roman numeral from II to X sits beside a player's
second kill of a life and up, and a row you are in, either end of it, gets a red
hairline round it. Rows leave after seven seconds.

It is its own plugin because every PeekSeek server wants one and only the debug server
runs Siege. It knows nothing about rounds: a streak is kills in one life, which on
Siege is the same thing as a round, and elsewhere is what a streak means anyway.

## What it needs

The **PeekSeek Kill Feed** workshop item, built from `plugins/peekseek-siege/ui/`
alongside the Siege screens (`Assets/PeekSeekKillFeed/`), listed in the server's
`WorkshopDownloadConfig.json`. The plugin's `EffectGUID` defaults to the effect in that
item, so a server with the item shows the feed and one without logs a line and shows
nothing. There is no chat form of a kill feed worth having.

Weapon pictures come from the site, `ItemIconURL`, and are turned into silhouettes by a
shader in the bundle, so the site's ordinary coloured icons are all that is needed.

## What it shows for each death

Unturned reports how somebody died, not with what. A gun or melee kill is pictured as
whatever the killer was holding; a grenade as the frag unless they were holding a
throwable; a rocket as the launcher; a charge, landmine, barbed wire and sentry each as
that item. Falls, the arena and the like show no picture. A death nobody caused still
gets a row, with the killer's side empty, because a team wants to know it is one down.

## Where things are

```
plugin/PeekSeekKillFeed.cs        Rocket entry point
plugin/KillFeed.cs                Unturned's death event turned into rows, and the rows drawn
plugin/Feed.cs                    the rows, the streak, the badge and the wording, testable
plugin/KillFeedConfiguration.cs   the effect, the key, the icon URL, how many rows for how long
tests/                            the feed's arithmetic
```
