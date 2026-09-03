# Search and movement

Why the search is budgeted, why a group does not search, and the traces that
changed each. Current code: `BudgetedSearch`, `SearchWorkspace`,
`MovementSystem`, `GroupDoctrine`.

---

## Field following — 2 September 2026

An order to a group used to be an order to every member of it, separately. Each
one planned a space-time route **to the destination cell**; only one can hold
that cell, so every other member's search exhausted the whole window before
returning "walk to the crust and stop". On the 200-unit arena that is 199 units
doing it, repeatedly.

The finding did not come from the metrics table. `SettlingReport` had said the
arena was expensive for days and could not say why. It came from reading the
baited-patrol replay tick by tick and noticing planning rings at the approach.

The replacement: a group's members descend one shared `DistanceField`, emitting
`[here, next]`. `DistanceField.Build` is a forward Dijkstra from the destination
and is valid as cost-to-go only because the movement rules are symmetric.

| | before | after |
|---|---|---|
| arena-200 settle | 586 ticks | 388 |
| arena-200 search nodes | 5.52M | 1.66M |
| group route ratio | 1.396 | 1.076 |
| chokepoint | 1.248 | 1.136 |
| reconcile | 1.336 | 1.099 |
| patrol approach | 14 ticks / 21 steps / 2 crossings | 7 / 19 / 1 |

### The full reading, as of field following

On this machine, with `FollowBlockedTicks` at 12: arena-200 settles at 388 ticks
for 1.66M nodes; the throng packs to 3.41, the ideal exactly; route ratios run
headon 1.000, group 1.076, crosscut 1.049, chokepoint 1.136, crossing 1.240,
standing 1.122, staggered 1.085, throng 1.450, countermand 2.382, reconcile
1.099; blob retreats 0, 1, 0; the benchmark lands 126 of 128.

Two figures are known trades: crossing is 1.240 where 1.209 was reachable, and
the arena would settle in 326 at a blocked-ticks threshold of 8 — but at 8 a
settled blob retreats 2 and the throng departs over 26 ticks.

*A machine and a moment. The ceilings in the tests are what actually bind.*

### Choosing `FollowBlockedTicks`

Everything else equal, on the settling report:

| threshold | arena | what breaks |
|---|---|---|
| never | never settles | throng deadlocks |
| 8 | 326 | settled blob retreats 2; throng departs over 26 ticks |
| **12** | **388** | every ceiling holds |
| 16 | 446 | — |
| 32 | 523 | — |

The search is what unsticks a big crust, and every tick of waiting for it is
paid by two hundred units at once; too eager and the detours it plans round a
packed rim read as retreats.

### Where `SettlingReport` came from

Written mid-spike, after two plausible changes to the claim pass were each
reverted: the first showed up as units retreating from a settled blob, the
second as a throng packing loosely. In both cases the failing test named a
symptom rather than the trade. Seeing arena settle time, packing tightness, ten
route ratios and the benchmark boundary move together turns that into a
decision.

---

## Why the search is budgeted — milestone 1

A single search expanded **594,349 nodes on a 1,048,576-cell map**. At any
realistic node rate that is more than a 60 Hz frame: one unit, one order, one
dropped frame, before any multi-agent work is added.

## The generation stamp — the number depends entirely on path length

Clearing three grid-sized arrays costs the same on a search that expands three
nodes as on one that expands forty thousand, so `SearchWorkspace` bumps a
counter instead and makes staleness a comparison rather than a write.

**The aggregate hides what it is worth.** Over the 155,620-record Moving AI
corpus the whole change is worth 3%, because 73% of that corpus is long
cross-map problems averaging 18,339 expansions — only 20 cells cleared per node.
Banded by the scenario files' own difficulty bucket it reads differently: the
shortest problems average three expanded nodes and **44,306 cells cleared for
each one**. Restricted to short paths — which is what most movement in a game
actually is — it is **~4.9× faster: 101,520 searches a second against 20,529**.

A good reminder that an average over a corpus can be the wrong question asked
precisely.

---

## Traces that changed a rule

**The three-cycle — 2 September 2026.** The cross-swap pass matched a *mutual*
cross only: A on B's slot and B on A's. A probe found three members of
twenty-four permanently stalled in a sealed column — 3 stood on 8's slot, 8 on
5's, 5 on 3's. No pair of them crosses, so the pass saw nothing;
`SettleWhereYouStand` refused because every cell underfoot was claimed; the
reconcile pass had no reachable vacated cell to offer. The rotation was stable
and permanent. Reading it as a cycle rather than a pair is the fix, and 400
swept runs have since found no cycle under the shipping steerer.

**Metering at order time.** Chokepoint metering first engaged the moment the
order was issued, and froze the tail half a chamber from the gate. Every batch
then paid the full transit latency serially — a measured **4× slowdown**.
Metering now turns on at *contact*: door discipline starts at the door.

**The 8-tick stall timer.** The headon trace showed it re-asking an unchanged
question at 196 nodes a probe. Retries are event-driven now — an arrival, an
order, a reconciliation — and the timer is a 64-tick backstop that turns a
missed wake into slow rather than frozen.

**Replanning only on expiry.** Waiting for a plan to run out before starting the
next search means the agent stands for the whole anchor while a finished plan
waits to be allowed to begin. Measured on the patrol's approach: three units a
single step from their places at tick 11, the last of them not taking that step
until tick 16.

**A ring that could not be rejoined.** `Recall(agent, alongside)` did not regrow
the formation's ring to its new member count, so a returning unit sat beside a
full ring: a member with no slot that makes no progress waits four backstops for
its next attempt, on the premise that a claim will wake it sooner — and with
every slot held, no claim ever came. Measured at **256 ticks of nothing**.

**Settling in a doorway.** A follower stopped in a gap behind a fellow reads as
"not moving, close enough". The first settle rule parked it there and froze
everyone behind it — hence `IGroupView.IsDoorway`.

**Turning round at the backstop.** Group members used to march outward from the
backstop rather than settling, and the rear of a group used to fail its first
plan outright.
