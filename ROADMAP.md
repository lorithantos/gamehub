# Roadmap

`main` · 623 tests green · 3 September 2026

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
| Board | one shared grid; the movement system broadcasts placed, stepped, removed, and the world listens |

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

---

## Next, in dependency order

### 1. Reservation ownership

Two sides in one movement system cooperate in pathing: one table, no owner,
so an attacker plans against the guards' futures and yields. A line can be
held by fire and never by body. Give the table an owner, let other sides
enter planning as observed occupancy only, and resolve conflicts at
execution time, where the step check already refuses an occupied cell.
Croatia is lines across outlets; this comes first.

### 2. Fog as a filtered broadcast

Limited perception on the board. A side's view hears only the events its
units could have witnessed. The broadcast is built; the filter is not, and
neither is what a side does about a unit it last saw three ticks ago.

### 3. Balance the region partition

90 regions sounds like a partition and is mostly slivers plus **one region
holding 46% of the map** — a search inside it is still most of a flat search.
Probably wants cutting only at gates that divide evenly, rather than at every
gate found. Everything hierarchical waits on this being worth having.

### 4. The disengage verb

Specified, cheap, and now affordable: step outside the threat's reach and
hold, rather than errand to a pad. No pad contention, and it makes a high
retreat threshold cheap. Self-heal already exists as a rate.

### 5. Shielding

A veteran makes the rookies beside it safer and slower to earn. The tactical
trade the whole rank system implies. **Do not build half of it** — the
earn-slower half alone makes a veteran a pure penalty to everyone near it.

### 6. Hierarchical planning

Abstract A* over regions, refined locally. Cheap once the partition is balanced.
**Keep the two claims apart**: flat search is optimal and is validated against
published optimal costs; region routes are near-optimal by design. If those merge,
the first hierarchical path reads as a regression in a test green since milestone 1.

### 7. Speeds

`Movement` becomes per-unit; the fastest unit moves a cell per tick and others
wait. Note this solves the ~3× spread between unit types and **not** absolute
pace — that was the 10× error, and calibration already fixed it.

### 8. The Croatia fixture

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

- **Enemies cooperate in pathing.** One reservation table, no owner. See
  item 1.
- **Credit is per fraction of the victim.** A tank kill earns what a rifleman
  kill earns. Units have hit points now but no cost, and the C&C3 rule
  weights by cost.
- **Damage is a continuous rate.** No burst, no cooldown, so the rocket bike
  that motivated threat targeting cannot actually spike.
- **The waves are too light and the reserve too tight** in the guard demo:
  every wave dies in under five seconds, and the reserve of four killed a
  buggy on its way to the pad.
- **`ChokepointScan`** is still what `MovementSystem` uses. `ContourGates` is
  better on every measure and is not wired in.
- **Both published artifacts are stale**, and the guard replay page's prose
  describes the demo before it had an enemy that shoots.

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
