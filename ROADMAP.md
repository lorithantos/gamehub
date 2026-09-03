# Roadmap

`main` at `a420653` · 597 tests green · 2 September 2026

The goal is **tactical doctrine**: units that hold a position, fall back to
repair, rotate, and get baited — the C&C behaviours that were missing. Movement
is the substrate, not the point.

This file is the short version. The working notes are janet thread items; the
narrative versions are *Inside the Tick* and *The Road to Croatia*, both of which
are now behind this file.

---

## Where we are

The substrate is strong and proven at real scale. The doctrine is real but young,
and until today it was tuned against maps and a clock that made its numbers
meaningless.

| | |
|---|---|
| Pathfinding | optimal, validated against published costs on real StarCraft maps |
| Group movement | field following — arena-200 went 5.52M search nodes → 1.66M |
| Doctrine | guard and patrol; retreat, rank, reserve, all playing unscripted |
| Scale | **pinned today** — 0.25 s/tick, 2 m/cell |
| Combat | damage table and attribution exist; nothing fires a weapon yet |

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

---

## Next, in dependency order

### 1. Balance the region partition

90 regions sounds like a partition and is mostly slivers plus **one region
holding 46% of the map** — a search inside it is still most of a flat search.
Probably wants cutting only at gates that divide evenly, rather than at every
gate found. Everything hierarchical waits on this being worth having.

### 2. Weapons that fire

The table exists and nothing consumes it. Units need a weapon and an armour
class, a range, and something that decides what to shoot. This is what turns
`DamageBy` from a tested function into the thing rank is actually earned from.

**It also retires a stand-in**: exposure-tick accrual measures *presence*, which
was only ever defensible because nothing could deal damage.

### 3. Self-heal, and the disengage verb

Both are specified, both are cheap, both were blocked on damage being a rate —
which it now is.

- **Self-heal** is a few lines in `DemoWorld.Settle`. It will delete the demo's
  tick-180 beat, so the script moves in the same pass.
- **Disengage** is a doctrine verb distinct from repair: step outside the threat
  radius and hold, rather than errand to a pad. Cheap, no pad contention, and it
  makes a high retreat threshold affordable.

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

- **Health is a fraction, not a quantity.** Units do not differ in how much there
  is to lose, which is why two config numbers have to know about each other.
- **`ChokepointScan`** is still what `MovementSystem` uses. `ContourGates` is
  better on every measure and is not wired in.
- **Both published artifacts are stale.** They describe the rotation demo as
  finished and predate everything from tonight.

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
