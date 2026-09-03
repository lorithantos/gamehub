# Scale and doctrine

What a tick and a cell are worth, and how rank, retreat and repair got their
rules. Current code: `WorldScale`, `Combat`, `RepairPolicy`, `DemoWorld`,
`GuardDoctrine`, `PatrolDoctrine`, `RadiusSight`.

---

## Fog as a filtered broadcast — 3 September 2026

Roadmap step 1. A side is told only what its own units could have witnessed,
and remembers what it can no longer see. Off by default; nothing is wired to
it yet, and the last section here says why that is the honest state.

### Decisions asked for before building

- **Sight is its own number on the kit**, not a multiple of range. A unit that
  sees further than it shoots is a scout; one that shoots further than it sees
  needs a spotter. Neither is expressible if sight is derived from reach.
  rifleman 6, buggy 9, tank 7, rocketbike 7, against ranges 4/5/6/6 — the
  buggy is the scout and is the whole argument for the separate number.
- **Plain distance, but behind a seam.** `ISight.CanSee(from, to, range)` with
  `RadiusSight` shipping. Walls do not block sight yet, which is knowingly and
  visibly wrong; a line-of-sight implementation drops in with no caller
  changing, and the difference between the two is then measurable.
- **A sighting is remembered, stamped with its tick**, and carries no "is it
  stale" flag. How old is too old is a doctrine's decision, and a patrol and a
  guard have every reason to answer differently.
- **A unit with no kit has no eyes**, and a fog world refuses to settle with
  one standing. Sight 0 is a real answer and almost never the intended one: a
  side blinded by a forgotten `Enlist` looks exactly like a doctrine that has
  stopped working.

### The watcher's own movement is the event

The first design here had a per-tick sweep, on the reasoning that a stationary
unit broadcasts nothing and so could never be discovered from the stream. That
was wrong, and the correction is the design:

> The movement of a unit is the thing that allows it to see more. So a unit
> discovered by movement is just normal events happening.

Visibility changes only when the board changes, and every board change is
broadcast — including the watcher's own step. The mistake was testing the
filter against the *subject* of an event only. A `_stale` flag set in `Hear`
on any event, resolved lazily on the first read, is the whole mechanism: a
quiet tick costs nothing, and a tick that raises fifty steps looks once rather
than fifty times.

One exception is marked rather than hidden. `HostileCells` is a list a demo
writes directly, with no event behind it, so a world holding any scripted
threats is re-looked once per `Settle`. A world of real units alone is purely
event-driven.

### `Hostiles` is what I see; `Sightings` is what I know

The split is what keeps every existing doctrine working unchanged. A doctrine
reading only `Hostiles` forgets an enemy the instant it loses sight of it,
which is exactly what every doctrine written before fog does.

A sighting is dropped when the cell it names is in plain view and the unit is
not on it — looking straight at where something was and finding it gone is
knowledge too. **Under a plain radius that turns out to mean a ghost only
survives if the *watcher* leaves.** Hold still while an enemy walks away and
every cell it was ever seen on is a cell you are still watching, so there is
nothing left to remember. The memory is real but largely dormant until
line-of-sight lands and a corner can hide something; it cost nothing to build
now, and it is precisely what LOS switches on.

### It is built, and it changes nothing yet

Measured rather than assumed, and worth knowing before anyone spends time
wiring it in:

- The guard demo with `fog: true` produced a headline **identical** to the
  fog-off baseline — 4/6 standing, 15/15 destroyed, 3 veterans, worst overrun
  0.26. The graph says why: `ISquadView.Hostiles` has exactly one production
  reader, `PatrolDoctrine.NearestHostileWithin`. `GuardDoctrine` never reads
  it, so fog cannot reach it.
- The patrol demo needs no run at all. Its leash is 5.0 and every kit's sight
  is 6 to 9, so anything inside the leash is already visible.

So fog is inert until a doctrine reacts to something further away than it can
see, or LOS makes cover real. Both are downstream.

`FogTests.EveryKitSeesAtLeastAsFarAsItShoots` is pinned **so that it fails
later**. While it holds, fog cannot make a unit fire at something it cannot
see, which is why `TargetFor` is not fog-aware. A line-of-sight `ISight`
breaks it — a wall blocks sight and not the range check — and that test going
red is how the problem arrives, rather than a unit quietly shooting through a
hill.

---

## Weapons that fire — 3 September 2026

Roadmap step 2, built in four commits, each green before the next: a death
verb in Nav.Core, sides in the world, kits and a fire pass, then rank read
from contribution and the guard demo replayed as two doctrines shooting at
each other.

### Decisions asked for before building

- **Hostiles are a second population** in the one movement system, not
  static emplacements. Both sides move under doctrine; that is what makes
  it AI against AI.
- **Blast hits everyone in radius**, own side included. Only the shooter is
  spared its own shot.
- **A unit shoots the highest threat in range**, where threat is how fast the
  target can hurt *me* right now: "a rocket bike is a better target than a
  level 6 tank — the tank may do more damage over the long run, but the
  rocket bike can do more damage quickly." So threat is the table lookup
  `Damage(theirWeapon, myArmour) × theirRate`, per observer. Writing the
  tests found that the same pair flips: to a tank the rocket bike is the
  danger over the buggy (57.8 against 10.5 hit points a second); to a
  rifleman the buggy is (21.0 against 15.8).

### Exposure-rank retired

Rank had climbed a count of ticks spent within a radius of a hostile cell.
That measured presence, and was only ever defensible because nothing could
deal damage. `RankOf` now reads contribution: damage dealt through
`DamageBy`, plus a bonus for the killing blow scaled by the victim's rank.
The exposure count is still kept, because the aura damage keys off it and a
replay can ask it; it earns nothing. The `perExposedSecond` key is gone.

### Hit points arrived with the kits, not as their own item

`baseDamage` had been 1.0 to 3.5 against health that was a 0..1 fraction, so
every weapon killed everything in one shot and the versus table was
decoration. Each kit carries `hitPoints`; `DamageBy` takes hit points and
divides by the target's; a unit with no kit has one, so every earlier test
is exact. `HealthOf` on the seam stays a fraction and every threshold keeps
working. Credit is still in fractions of the victim — a tank kill is worth
what a rifleman kill is — until units have a cost to weight it by.

The per-shot numbers were chosen for a legible table and nothing else. From
`FireTests.TimeToKillForTheRecord`, seconds to kill at a direct hit:

| shooter \ target | rifleman | buggy | tank | rocketbike |
|---|---|---|---|---|
| rifleman | 4.2 | 28.6 | 333.3 | 21.4 |
| buggy | 2.4 | 4.0 | 38.1 | 3.0 |
| tank | 4.2 | 5.3 | 13.3 | 4.0 |
| rocketbike | 3.2 | 3.8 | 6.9 | 2.9 |

Damage is a **continuous rate** — shots per second times seconds per tick —
rather than discrete shots with a cooldown. Cheaper and deterministic, and it
loses the burst that made the rocket bike the user's example. Noted, not
built.

### Shots are decided before any lands

Every shooter picks its target from where things stood at the start of the
pass; the shots then resolve in shooter order. Two units that would kill each
other both die, and `Fallen` lists them in landing order. This is also what
keeps last-hit attribution deterministic: the same tick always resolves the
same way.

### The world learns the board from a broadcast

The first version of sides had `DemoWorld` copy every agent's cell out of the
system each settle, with a priming rule at tick zero. That was a second copy
of the truth. Asked whether the seam wanted reconsidering, Lori: "we want
broadcast events. This is better than opening up seams. The grid can remain
shared, but consider that we will want fog later."

The first cut was an append-only journal read from a cursor, chosen for
determinism and for late readers. Lori: "Why not use the built in
broadcasts with registration? This is single threaded." Right — the cursor
was ceremony, and the journal grew without bound. So `MovementSystem.Happened`
is a plain event carrying `(tick, kind, agent, cell, from)` — placed,
stepped, removed — with steps raised at the end of the tick once every unit
stands where the tick put it. `DemoWorld.Listen` registers once and reads
nothing else from the system afterwards.

The one hazard of a callback, a handler calling back into the system
mid-tick, is handled where Lori said it should be: the system puts any verb
it receives during a tick on its own list and applies it at the head of the
next tick. A reaction to a step can never distort the pass that produced
it. `AddAgent` is the exception and refuses, because it answers with an id.

Fog is a listener that passes on only what a side could have witnessed.

### Measured on the day

The guard demo: six guards (four tanks, two buggies) against three waves of
two rocket bikes, two riflemen and a buggy, at ticks 0, 110 and 220.

    4/6 guards standing, 2 lost; 15/15 attackers destroyed; 3 veterans;
    2 rotated through repair, never more than 1 away against a reserve of 4;
    worst overrun 0.26

Every wave was destroyed within 13–17 ticks of arriving. Both guards lost were
the buggies. Guard 1 fell back at 0.14 health, 0.26 past its threshold,
because two were already away against a reserve of four, and died on the
walk. The waves are too light and the reserve too tight; both are numbers.

### Found and left open

Two sides in one movement system **cooperate in pathing**. There is one
reservation table with no owner, so an attacker plans against the guards'
reservations up to 32 ticks ahead and yields to them. Two armies here can
never block each other: a position is held by fire only, never by body. It
did not show in this demo because the waves stand off and shoot. It will
show the first time an attacker tries to push through a line, and Croatia
is nothing but lines across outlets. The journal does not address it.

---

## Calibration — 2 September 2026 (`05a91f9`)

Unit speed was a **derived accident** rather than a chosen quantity. Movement is
one cell per tick; a tick was a sixtieth of a second because that is what a
render loop runs at; nobody multiplied the two. Units crossed ground at sixty
cells a second — with a cell at two metres, **432 km/h**. The whole guard demo —
six promotions, four repair trips and a rotation — happened in **five and a
third seconds**.

It surfaced from the other end: the maps were too small to represent a real
battle, and asking "how long should crossing a 512-square take" made the clock
answerable. Two minutes was the target.

Pinned: `secondsPerTick = 0.25`, `metresPerCell = 2.0`, in `config/scale.ini`.
Cells per second falls out of the movement rule. All three read plausibly at
once — 512 cells in 2:08, 1.02 km of ground, 29 km/h — which is the check that a
calibration is honest rather than merely consistent.

**Seven tests went red and every one of them was right**: they had encoded 60 Hz
as an assumption. Rewritten to state their own step and count ticks rather than
frames.

The correction was ~10× on absolute pace. It does **not** address the ~3×
spread between unit types; that is a separate piece of work.

---

## Rank and the repair reserve — 2 September 2026 (`a420653`)

### The reserve is ordered lowest rank first

The first version gave the scarce repair place to the **highest** rank, on the
reasoning that a veteran is worth more and should be preserved. That is
backwards, and the correction is a design rule rather than an economic argument:

> We don't want to reserve advancement, we want to accelerate it.

The mechanical reason veterans stay on the line: **they earn more**. At full
rank they also self-heal. Rookies alongside them earn more slowly but are safer.
So the scarce place to leave the line goes to the lowest rank present, and the
veterans keep earning.

Two economic rationales were invented for this rule before the mechanical one
was asked for, and both were wrong. Ask what the mechanic is; it generates more
than a story about value does.

### Turn-round in the guard demo was arithmetically impossible

The demo was supposed to show a unit repairing and returning to the line. It
could not, and no tuning of the thresholds would have fixed it:

    damage × walkTicks  >  returnAbove − retreatBelow(veteran)
    0.004               vs  the 0.0067 required

The unit could not heal enough on the trip to clear its own return threshold.

### Last hit as the attribution rule

Whichever unit was resolving damage at the moment of death takes the kill bonus.
The objection — that it rewards the wrong unit — is about **variance, not
bias**: bigger numbers are more likely to land the death blow, so over many
kills the credit tracks the damage.

Measured over 400 targets: a unit dealing ~75% of damage took **75.5% of kills**.

Only damage that *landed* earns anything, so overkill pays nothing and everyone
still shooting at a corpse earns nothing.

A dedicated RNG so a late-built heavy unit is not consistently out-won is a real
want, and parked: it trades away determinism, which every replay depends on.

---

## The damage table

Five weapons × four armour classes, ours, in `config/combat.ini`. Prior art was
read for *shape* — that a weapon-versus-armour matrix makes composition beat
arithmetic, which goes back to Dune II — and never for values. None of the
numbers come from anyone else's game.

Rates are per **second**, never per tick. A rate written per tick silently
rescales the entire game when the tick changes, and the tick changes by editing
one line of a config file.

### Config does not fall back silently

`Ini.FromFileOrEmpty` plus per-key defaults means a typo'd path runs the
simulation on numbers nobody chose. `Ini.FromFile` throws, and `Ini.Defaulted`
records every key that fell back, so "we are running on defaults" is a question
with an answer.
