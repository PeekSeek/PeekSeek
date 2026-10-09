# Security

## Reporting a vulnerability

**Do not open a public issue.** Use GitHub's private reporting on this repository
(Security → Report a vulnerability), or email **peekseek@kcvdh.com**.

Tell us what you found, how to reach it, and what it gets you. A proof of concept against
your own account or your own instance helps; one against somebody else's does not, and is
not something we will act on in your favour.

What to expect:

| | |
| --- | --- |
| First reply | within 3 days |
| Assessment, with a verdict and a rough timeline | within 7 days |
| Fix for something exploitable against the live site | as fast as we can, usually days |
| Public disclosure | after the fix is deployed, crediting you unless you ask otherwise |

There is no bug bounty. This is a community project.

## What is in scope

The code in this repository, and `peekseek.net` as it runs that code. Most of what matters
sits in a few places, so these are worth saying out loud:

- **The game-server API.** Every `/server/v1/*` route authenticates by source IP alone
  (`AuthorizedServers`). Anything that lets a caller pass that check from an address not on
  the list is serious. See the `--forwarded-allow-ips` warning in
  [self-hosting](https://peekseek.net/docs/self-hosting/), which is the shape this has
  taken before.
- **Session identity.** A session is a UUID. Anything that leaks one, fixes one, or
  rotates somebody else's is in scope.
- **Steam and Discord OAuth.** Anything that lets one account be linked or acted on by
  another.
- **Moderation state.** Bans, IP records and permissions, including anything that lets a
  non-admin reach an `/admin` route or the bot's commands.
- **Anything that gets one player's data into another player's page.**

## What is not

- Reports from automated scanners with no demonstrated impact.
- Missing hardening headers on their own. The Content-Security-Policy on the site is
  permissive and we know it; a report needs to show what that permits.
- Denial of service, volumetric or otherwise. Please do not test it.
- Social engineering, physical access, or anything aimed at a player rather than the
  software.
- Vulnerabilities in Unturned, RocketMod or Steam. Report those upstream.
- Self-hosted instances somebody else runs. Tell them, not us.

## Testing

Use your own accounts and, where you can, your own instance. The whole site runs from
one container and a Postgres, which is what
[self-hosting](https://peekseek.net/docs/self-hosting/) is for. Against `peekseek.net`,
stay inside your own account, do not touch other players' data, and do not run anything
that degrades the service for people trying to play.

## Supported versions

The deployed site, and the latest tag of each plugin. Nothing older is patched: the
repository is the version, and `main` is what is released.
