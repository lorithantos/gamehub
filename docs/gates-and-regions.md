# Gates and regions

Finding where a map is narrow, cutting it there, and what routing over the
result costs. Current code: `ContourGates`, `Regions`, `RegionGraph`.

---

## Balancing the partition — 2 September 2026 (`98b02dd`)

Cutting at gates alone gives a decomposition, not a useful one. Measured on a
generated 256-square: **90 regions, median size eight, largest holding 46% of
the open map.** One number hiding two different faults.

**Slivers.** Two gates near each other carve the strip between them into its own
region. It is a region by the letter of the rule and nothing anybody would plan
through.

**The room nothing cuts.** A gate is where the map is *narrow*, so a large open
space contains none by construction and survives whole. A search inside it is
most of a flat search, which is the entire saving gone.

Merging fixes the first and cannot fix the second; splitting fixes the second
and would leave the first. Hence both, in that order. The split is *geometric* —
two poles by double sweep, every cell to the nearer pole by distance through the
region — and admits it: there is no semantic cut to be found in an empty room,
which is the honest reason the gate decomposition alone was never going to
balance.

| | before | after |
|---|---|---|
| largest region | 11,816 cells (46% of map) | 1,020 (4%) |
| median size | 8 | 38 |
| regions | 90 | 108 |
| links | 107 | 133 |
| build time | — | 172 ms |

Route quality after balancing, 243 cross-region pairs, none unroutable: median
**1.047** of the flat optimum, mean 1.074, worst 2.571. Unrefined — the route is
forced through one representative cell per gate, exactly; the worst case is what
refinement is for.

### The measurement had to be rebuilt first

The original route stand-in took the single best gate directly joining the two
regions and gave up otherwise. Fine while regions were few and huge. Once
balancing produced 108 of them it could not route **232 of 244** sampled pairs,
and the ratios it did report were measuring its own limits rather than the
abstraction's.

The replacement is Dijkstra over **links, not regions** — the line graph. What a
region costs to cross depends on which gate you came in by and which you leave
by, so a node that is only "a region" cannot carry the cost. Getting this wrong
is how a hierarchical planner quietly returns routes far worse than it should,
and it looked exactly like the abstraction being bad.

---

## Why `ChokepointScan` was replaced — 2 September 2026 (`a713831`, `b411283`)

`ChokepointScan` asks a **global** question about a **local** structure: it
requires a cell to carry a tenth of all traffic on the whole map, which only
happens where the map is nearly one corridor.

On a 512×384 map that is 62% wall it found **nothing at all, at any sampling
density** — because raising the terminal count raises the threshold in exact
proportion to the counts it is compared against. Three wrong theories were tried
before that landed:

1. *Undersampling.* Killed by a sweep over terminal counts: more terminals, same
   nothing.
2. *The threshold is the sole fault, the width test is fine.* Half right. The
   width test alone has roughly 99% false positives.
3. The share-of-global-traffic criterion itself, which is the answer.

`ContourGates` scores narrowness instead, which is a property of the passage
rather than of the map around it. Against the generator's own known passages it
found **14 of 14**, against the shipped scan's **3 of 14**, and ran **20–3000×
faster**.

**Still not wired in.** `MovementSystem` uses `ChokepointScan` for metering. The
replacement is better on every measure and the swap has not been made.

---

## Scoring a detector against ground truth — 2 September 2026 (`94a9b5c`, `736049d`)

The fixtures under `maps/fixtures` are hand-drawn and tiny — largest 49×49,
against 384×384 for the smallest map in a published benchmark set. Doctrine
tuned at that size produces thresholds that transfer to nothing.

`MapGenerator` exists so passages are *known*, because we cut them, and a
detector can be scored rather than eyeballed against a screenshot.

Two corrections during its build:

- **The oracle contradicted itself.** `SmallerSide` and `Detour` were computed
  independently and then asserted to agree. Fixed by deriving the detour from
  the same connectivity flood.
- **The first generator reproduced the arena**: 83.2% open against arena's
  85.5%. The arena is deliberately open and is a bad target. Retuned to 33.9%.
