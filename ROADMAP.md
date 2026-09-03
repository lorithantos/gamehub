# Roadmap

`main` · 632 tests green · 3 September 2026

The goal is **tactical doctrine**: units that hold a position, fall back to
repair, rotate, and get baited — the C&C behaviours that were missing. Movement
is the substrate, not the point.

This file is the short version, and it looks forward. [`docs/`](docs/) looks
back: what each decision replaced, what was tried, and what the numbers were on
the day. The working notes are janet thread items; the narrative versions are
*Inside the Tick* and *The Road to Croatia*, both of which are now behind this
file.

---

## Where we are

The substrate is strong and proven at real scale. The doctrine is real but young,
and until today it was tuned against maps and a clock that made its numbers
meaningless.

| | |
|---|---|
| Pathfinding | optimal, validated against published costs on real StarCraft maps |
| Group movement | field following — arena-200 went 5.52M search nodes → 1.66M |
| Doctrine | guard and patrol; retreat, rank, reserve, all playing unscripted, on both sides |
| Scale | pinned — 0.25 s/tick, 2 m/cell |
| Combat | **weapons fire** — kits, hit points, blast, highest-threat targeting; rank earned from damage |
| Board | one shared grid, one reservation table per side; the movement system broadcasts placed, stepped, removed, and the world listens |
| Perception | **fog, on in the guard demo** — sight per kit behind a seam, pads that watch their own ground, sightings remembered with their tick |

---

## Done

- **Validated park.** A sound "hold this cell", with the reservation table able to
  refuse. Everything settling stands on it.
- **Field following.** A group descends one shared distance field rather than
  every member searching to the same cell. The single biggest win, and it came
  from reading a replay tick by tick.
- **Crossed claims as cycles**, not just pairs. Free insurance — 400 swept runs
  found no cycle under the shipping steerer.
- **Rank, earned not assigned.** `RankOf` on the perception seam; a rank-indexed
  retreat table; a reserve the line will not go below.
- **The guard demo plays rather than being staged.** No `SetHealth` anywhere:
  exposure costs health and earns rank, and every casualty is a consequence of
  where a unit stood.
- **World calibration.** 512 cells in 2:08, a kilometre of ground, 29 km/h.
- **Combat table and attribution.** Five weapons × four armour classes, ours;
  credit by last hit, measured unbiased at 75.5% of kills for 75% of damage.
- **Map generator** at real scale that knows its own answers, so a detector can
  be scored rather than admired.
- **Gate detection** that works on real maps — 14/14 against the shipped scan's
  3/14, and 20–3000× faster.
- **Region graph.** 26,208 cells → 90 regions; routing through gates costs a
  median 1.8% over optimal.
- **Viewport.** The viewer no longer goes blind above about 100×100.
- **Weapons that fire.** Kits from config, hit points per type, a shot per
  tick at whatever in range can hurt *you* fastest, blast that spares nobody
  but the shooter. Rank reads contribution; exposure accrual retired.
- **Death, sides, and the broadcast.** A unit can leave the world and the
  living carry on; each side perceives the other's living units as hostiles;
  the movement system raises placed, stepped, removed, the world learns the
  board from that alone, and a verb issued mid-tick waits for the next.
- **The guard demo is AI against AI.** Three waves under GuardDoctrine
  against a line under GuardDoctrine. 4/6 standing, 15/15 destroyed, 3
  veterans, all of it earned.
- **Reservation ownership.** A table per side; an enemy is ground, not a
  plan; conflicts between sides settle at the step. Enemies advance until
  they meet, a line of bodies holds a corridor, and one commander's world is
  untouched.
- **Fog, as a filter on the broadcast.** Sight is its own number per kit
  behind an `ISight` seam, so line-of-sight replaces a radius without a caller
  changing. `Hostiles` is what a side can see; `Sightings` is what it knows,
  stamped with the tick, with the forgetting policy left to doctrine. Driven
  by events and not by a clock — the watcher's own step is what discovers a
  unit that has been standing still all along.

---

## Next, in dependency order

### 1. A plan through fog should be tentative

The filter is built and the guard demo runs under it. What is still wrong is
the *planner*: `MovementSystem.SideView` treats every cell another side
occupies as blocked, read from true occupancy, whether or not the planning
side can see the unit standing there. So perception is honest and planning is
omniscient, and the two disagree.

A unit should be able to aim at a destination in or beyond the fog and hold
only a tentative plan through it, corrected on contact — which is the same
bump-stop-replan the reservation work already points at. `Nav.Core` must not
learn what perception is, so the filter is handed **down** as a per-side
predicate rather than reached for upward.

Terrain is a separate question and nobody has answered it: walls are fully
known today. That is the ordinary fog split, as against shroud. Do not make
the planner terrain-blind by accident — the published-optimal-cost validation
is measured against a fully known map.

### 2. Balance the region partition

90 regions sounds like a partition and is mostly slivers plus **one region
holding 46% of the map** — a search inside it is still most of a flat search.
Probably wants cutting only at gates that divide evenly, rather than at every
gate found. Everything hierarchical waits on this being worth having.

### 3. The disengage verb

Specified, cheap, and now affordable: step outside the threat's reach and
hold, rather than errand to a pad. No pad contention, and it makes a high
retreat threshold cheap. Self-heal already exists as a rate.

### 4. Shielding

A veteran makes the rookies beside it safer and slower to earn. The tactical
trade the whole rank system implies. **Do not build half of it** — the
earn-slower half alone makes a veteran a pure penalty to everyone near it.

### 5. Hierarchical planning

Abstract A* over regions, refined locally. Cheap once the partition is balanced.
**Keep the two claims apart**: flat search is optimal and is validated against
published optimal costs; region routes are near-optimal by design. If those merge,
the first hierarchical path reads as a regression in a test green since milestone 1.

### 6. Speeds

`Movement` becomes per-unit; the fastest unit moves a cell per tick and others
wait. Note this solves the ~3× spread between unit types and **not** absolute
pace — that was the 10× error, and calibration already fixed it.

### 7. The Croatia fixture

A map with room, scripted waves, and a hold-verdict over defended objects with
health. Medium to build, open-ended to tune, which is the point.

---

## Parked, with a reason

- **Replay page compositing.** Blank canvas in the artifact viewer. Leading
  hypothesis: the old code floored cell size at 8px and the rewrite replaced that
  floor with one derived from `clientWidth`, which an unlaid-out iframe reports
  as zero. Reverted; approach still right.
- **Replay pages → app or server.** They are a second renderer, and every feature
  gets written twice. Decide on which is the better *instrument*, not which is
  easier to share.
- **Dedicated RNG for attribution.** So a monster tank built late is not
  consistently out-won by veterans who take the blows off it. Real problem, but
  it trades away determinism — wait until a demo shows it. If it comes, the
  stream must be its own.

---

## Known wrong

- **A contested cell goes to the lower id.** Deterministic and the system's
  usual tie-break, but it favours whichever side was placed first. A fairer
  rule wants a reason, not a coin.
- **An enemy's cell is held for the whole window** when planning around it,
  though it may leave next tick. Conservative; costs a replan.
- **Credit is per fraction of the victim.** A tank kill earns what a rifleman
  kill earns. Units have hit points now but no cost, and the C&C3 rule
  weights by cost.
- **Damage is a continuous rate.** No burst, no cooldown, so the rocket bike
  that motivated threat targeting cannot actually spike.
- **The waves are too light**, and the enlarged demo makes it unarguable:
  18/18 attackers destroyed for one guard lost. Still not to be tuned until
  retreat and casualty response land, because both change the answer.
- **A side plans around enemies it cannot see.** `SideView` blocks on true
  occupancy, so fog limits what a doctrine knows and not what a planner uses.
  This is next.
- **The patrol-bait page and its artifact are stale.** The guard page was
  rewritten from its own trace and republished; the patrol one has not been.
  A demo is not updated until its prose is: the refresher rewrites only the
  `trace-data` block, so every readout and paragraph outlives a demo that no
  longer resembles it, with live numbers under wrong words.
- **`ChokepointScan`** is still what `MovementSystem` uses. `ContourGates` is
  better on every measure and is not wired in.
- **A replay draws the true board, not either side's view of it.** The units
  are under fog; the camera is not. Drawing what a side actually knows is the
  next thing that would make a replay worth reading on a map this size.
- **The enemy ring is gone rather than replaced.** It drew the exposure radius
  and the legend called it the ring rank is earned inside, which stopped being
  true when exposure-rank was retired. What belongs there is each unit's own
  weapon reach, which needs the kit in the trace.

---

## What has held up

- **Measure before deciding.** Every design call today that survived was one with
  a number attached.
- **The metrics table shows the cost and never the cause.** `SettlingReport` said
  the arena was expensive for days; watching a replay said why, and that was worth
  3.9M search nodes. The viewer is an instrument, not a presentation layer.
- **Silent degradation is the recurring bug.** A scan that answered nothing rather
  than failing, a viewer that drew at one pixel per cell rather than refusing, a
  page that played a demo which no longer existed, a config that would have run on
  numbers nobody chose. Each looked like working code.
- **Own nothing borrowed.** Prior art is read for shape and never for values;
  benchmark maps stay downloaded, never committed; nothing we publish carries
  someone else's level design.
- **Broadcast rather than open a seam.** When a layer above needed to know
  the board, the answer was an event it registers for, not a hole in the
  movement system and not a second copy of its state. The system defers any
  verb it hears mid-tick to the next one, so listening is safe. Fog falls
  out of the same shape.
