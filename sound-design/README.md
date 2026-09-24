# PeekSeek sound design concepts

Thirteen sounds, three concepts each. Most folders hold `A_bell.mp3`, `B_tactile.mp3` and
`C_arcade.mp3`. The letter is a family: pick one and those sounds come from the same
instrument and fit together. The kills are different: they are impacts, not melodies,
and have their own three concepts (`A_steel`, `B_punch`, `C_boom`). `make_sounds.py`
renders everything (numpy + ffmpeg). `python make_sounds.py kill round_start` re-renders
only the sounds you name, so a sound you already like never changes.

| Folder | When it plays | File length (tail to silence) |
| --- | --- | --- |
| `kill` | You get a kill | 0.8 s |
| `kill_streak` | Demo of kills 1 to 5 in one round, 0.7 s apart | 4 s |
| `round_win` / `round_loss` | Your team takes or drops a round | 1.6 s |
| `match_point` | A team is one round from winning | 1.6 s |
| `match_win` / `match_loss` | End of match | 2.3 to 3 s |
| `queue_join` / `queue_leave` | On the site, you enter or leave the queue | 0.7 s |
| `match_found` | The queue pops | 2 s |
| `round_start` | 5, 4, 3, 2, 1, go | 6 s |
| `time_10s` | 10 seconds left on a clock (picks, a timed round) | 0.8 s |
| `pick_lock` | A kit pick or ban locks in | 0.7 s |

## What makes a sound feel like a reward

**1. It has to be tied to *your* action, instantly.** The reward sound works as a
conditioned cue. Schultz's dopamine work showed that once a cue reliably predicts a reward,
the dopamine response moves off the reward and onto the cue itself. After a few hundred
kills the *sound* is the reward. This only works if it fires under ~50 ms after the kill
and never plays for anything else. Blizzard's "Play by Sound" talk for Overwatch aimed for
exactly this: a sound so consistent that players react to it on reflex.

**2. Rising pitch reads as good, falling pitch reads as bad.** Listeners rate ascending
intervals as happier and more pleasant than descending ones, and the bigger the jump the
stronger the effect (*The Ups and Downs of Melodic Interval Appreciation*, Music
Perception 2025). Major goes with rising, minor with falling. So every positive sound here
goes up and every negative one comes down.

**3. A kill is an impact, not a jingle.** The Mario coin (B5 then E6: a short pickup,
then a landing on the home note) is great for *collecting* something, and the rest of the
set uses that shape. But a pickup note puts 45 ms of melody in front of the hit, and it
sounds like a coin. A kill has to sound like you *did* something to the round. So every
kill lands at sample zero with a body hit, and the pitch is only there to count.

**4. Escalation is what makes a streak addictive.** Valorant's kill sound is a bell that goes
up in pitch with every kill in the round, and players buy weapon skins partly for the kill
sound. Peggle does the same thing: every peg hit plays the next note of an ascending scale,
so a good shot literally climbs, and the win cashes it out with Ode to Joy. Climbing works
because each note sets up an expectation of the next one.

**5. Sound makes a result feel bigger than it was, so be honest with it.** Dixon et al.
(2014) had 96 gamblers play a slot simulator. With sound on, players overestimated how often
they won by 29%, against 18% with sound off. For PeekSeek that means: celebrate real wins
properly, and never put a win-like sound on something that isn't one. Loss sounds should be
gentle closures, not buzzers. Losing already stings. A punishing sound on top of that
causes tilt, and a player on tilt quits the queue.

**6. Juice is layers, not volume.** "Juice it or lose it" (Jonasson and Purho, GDC 2012)
turned a flat Breakout into a game people couldn't put down by stacking small
feedback layers. A kill here is four layers at once: a 25 ms **crack** (contact), an
overdriven **punch** falling from 170 to 45 Hz (weight you feel in the chest), a **ring**
(the pitch, which is the streak count) and a dark **tail** (the round heard it). Take any
one away and it feels cheaper.

## How it stays simple and doesn't get in the way

- **One key for everything: D major.** Every sound shares a key, so a kill ringing
  into a round win never clashes, and the whole set sounds like one brand.
- **Ladders use the pentatonic (D E F# A B).** It has no semitones, so a double kill
  where the second tail runs over the first is always consonant. That's why Peggle and
  most combo systems avoid the full scale.
- **Kill weight sits under the footsteps.** Footstep detail lives around 1 to 4 kHz.
  About half of each kill's energy is below 200 Hz (measured), where you feel it
  instead of it competing with footsteps. The part in the footstep band is the crack
  (25 ms) and the ring, which is gone in under half a second.
- **Kills are soft-clipped, not just turned up.** The peaks are rounded off with tanh,
  so the kill is dense and a little gritty at the same level as everything else.
- **Loudness follows importance** (measured, integrated LUFS): match found −15,
  round/match win −19, kill −21, 10 s warning −21, pick −24, round/match loss −26,
  queue leave −28. Loss is quieter than win. Match found is the loudest because it has
  to reach someone who alt-tabbed or walked away. Repetition does the rest: its motif plays
  twice before it climbs.
- **One motif, reused:** the rising fourth (A → D) is the first half of match found and
  the pickup into match win. Players learn one shape, and it means "good".
- **One ladder, reused:** the kill streak and the countdown climb the same five notes.
  Higher always means "more" or "closer".

## The ladders and notes

**Kill streak**, per kill in a round. The hit is the same every time, the ring on top
climbs, and each kill hits a little harder:

| Kill | Ring | What changes |
| --- | --- | --- |
| 1 | D | the base hit |
| 2 | E | harder |
| 3 | F# | a second layer joins (octave ring in A, sub punch in B) |
| 4 | A | harder still |
| 5 (ace) | B | a second hit 120 ms later, a fifth higher: the round is yours |

The three kill concepts:

- **A, steel.** Punch plus a struck steel plate (partials 1 : 2.76 : 5.4 : 8.9, the
  ratios of a real plate). The headshot "dink" family, so it reads instantly as "hit
  confirmed". Ring at D5 to B5.
- **B, punch.** The heaviest: a hard-driven body hit and a low overdriven power chord
  (root, fifth, octave, no third, so it can't sound cheerful), with a short high tick on
  top that carries the ladder. The most "weapon" of the three, the driest, and the one
  I'd ship.
- **C, boom.** Cinematic: a tuned floor-tom hit that falls onto its note, a dark noise
  tail, and a quiet ring an octave up. Biggest and roomiest. Great for an ace, maybe too
  big for every single kill.

**Round:** win is the D major arpeggio climbing D5 F#5 A5 D6 with a low D under it. Loss
falls A5 F5 D5. The F natural makes it minor, but it still lands home on D, so it reads
as "done", not "punished".

**Match point** is a suspended chord (D G A), pulsed twice and left unresolved. It's a
question. The next round's win arpeggio has the F# in it, so the suspension resolves on
its own when the round is taken. The two sounds tell a small story together.

**Match win** is a cadence: an A major chord (the dominant) sets up tension, then the
kill motif A → D, a climb to A6, and a full held D major chord. **Match loss** walks down
D6 A5 F5 D5 onto an open fifth (D and A, no third), neither happy nor sad. It's a
respectful close.

**Countdown (5 to GO)**, built to be read without looking at the screen:

- **The pitch climbs the kill ladder**, D E F# A B, one step per second. Higher is
  closer, so one tick tells you roughly where you are, even if you missed the start.
- **5 and 4 are quiet** (you're still getting ready). **3, 2, 1 get louder and each gets
  a thump**, so the familiar "3, 2, 1" stands out from the lead-in.
- **GO is the only one with a bass hit and a held chord** (D and A over three octaves,
  no third: firm, not cheerful). No one can mistake it for a tick.
- In game, show the number on screen on the same frame as each tick, so ear and eye
  learn it together. After a few rounds nobody needs to look.

**10 seconds left**: two quick pulses of A against B flat, over a low D. B flat is the
only note in the whole set outside D major, and it rubs against the A a semitone away.
That rub is what alarms use, so it reads as "hurry" without being a buzzer. It is under a
second and no louder than a kill, so it can land mid-fight. Play it once per clock: the
pick phase, and any gamemode with a timed round. ARENA has no clock, so it never plays
there.

**Queue**: join is a soft rising fifth D5 → A5. Leave is the same fifth falling and
quieter. Pick lock is a short D+A dyad with a D6 on top, plus the click and thump.

## The three families

- **A, bell.** FM glass bell, clean, a little air. Closest to the Valorant kill-bell
  feel. Reads premium and is easy to hear over gunfire.
- **B, tactile.** Dry marimba/woodblock with the strongest thump. Least musical, most
  "physical". CS-like restraint, and the least tiring after 500 kills.
- **C, arcade.** Soft pulse synth with a short slap echo. Most playful and brightest,
  closest to the website's energy.

My pick for a competitive game is **B_punch for kills**, **B for the rest of in-game**
(round, pick, countdown, 10 s), and
**A for the site and match-level moments** (queue, match found, match win). They share
the key and the motif, so mixing families still sounds like one game. Listen to
`kill_streak` in all three first: it's the sound players will hear the most.

## Shipping notes

- The mp3s are for listening. MP3 pads ~25 ms of silence at the start, which is
  noticeable on a kill sound. Use WAV or OGG in the Unity bundle. For the site, mp3 is fine.
- Don't add randomised pitch to the kill. Most games do that to avoid repetition
  fatigue, but here the pitch *is* the streak count, so random pitch would break the ladder.
  Vary the crack noise instead (the script seeds it; drop the seed per render).
- Reset the streak at round start, not on death. Kill 5 = ace only if the ladder counts
  kills within the round.

## Sources

- Schultz, dopamine reward prediction error: https://www.pnas.org/doi/pdf/10.1073/pnas.1014269108
- Dixon et al. 2014, sound in multiline slots: https://pubmed.ncbi.nlm.nih.gov/23821220/
- Ascending intervals yield more pleasure (Music Perception 2025): https://online.ucpress.edu/mp/article/doi/10.1525/mp.2025.2507514/214966/The-Ups-and-Downs-of-Melodic-Interval
- Pitch direction and major/minor perception: https://link.springer.com/article/10.3758/s13414-020-02198-6
- Overwatch, "Play by Sound", GDC 2016: https://www.gamedeveloper.com/audio/video-how-i-overwatch-i-was-designed-so-people-could-play-by-sound-
- Peggle 2 ascending peg scales: https://www.audiogang.org/peggle2-sonic-joy/
- Mario coin, B5 → E6: https://www.mariowiki.com/Coin_(sound_effect)
- Valorant kill audio: https://wiki.playvalorant.com/en-us/Kill_Banners
- Juice it or lose it: https://roblog.co.uk/2024/03/juicy-games/
- Footstep frequency band: https://attackshark.com/blogs/knowledges/fps-footstep-frequencies-2khz-4khz-mastery
