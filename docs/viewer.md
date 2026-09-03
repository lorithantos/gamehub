# The viewer

An instrument, not a presentation layer. Current code: `ViewerSession`,
`ViewerApp`, `GridLayout`, `TerrainImage`.

---

## Why it is worth the maintenance

The metrics table shows the cost and never the cause. `SettlingReport` said the
arena was expensive for days; watching a replay tick by tick said *why*, and
that was worth 3.9M search nodes — see
[search-and-movement.md](search-and-movement.md).

The user's own account of it:

> being able to see the work changes everything for me […] it was my looking at
> individual ticks and seeing odd behaviors that led to a bunch of improvements
> that cut staggering numbers of nodes off of the samples

And the reciprocal, which is the part that makes it a shared instrument rather
than a convenience: when the results are confusing, narrating what is on screen
tick by tick is how the fix gets found. That is how the group-movement defect
came out.

A caveat worth keeping honest about: a perfect oracle over the same data — a
node count per unit, say — could have found the arena defect too. That is
predicting yesterday's weather. The instrument is what was actually available.

---

## Going dark rather than failing — 2 September 2026 (`6de187f`)

The viewer never *failed* at scale, which is exactly what made the viewport
worth building. `GridLayout.Fit` floors at one pixel per cell, so a 512×512 map
renders — and every mark the viewer draws is sized from the cell, so units, ids,
health bars and routes all collapse into that one pixel.

**The map is visible and the simulation is not**, which is worse than failing,
because nothing announces it.

`GridLayout.Viewing` is the answer: a window over the map with a focus cell and
clamped anchor, so a 512-square is watchable a piece at a time.

This is the recurring bug in this project rather than a one-off. A scan that
answered nothing rather than refusing; a viewer that drew at one pixel per cell;
a replay page that played a demo which no longer existed; a config that would
have run on numbers nobody chose. Every one looked like working code.

---

## Where "what is loaded" lives — `ViewerSession`

Before the type existed it had no owner: it was smeared across constructor
arguments, two near-duplicate `Program.Main`s and `ViewerApp`'s private fields,
and the replay-restart code was session management hand-rolled inside an input
handler.

The line through the middle is deliberate and is still the contract. Content and
simulation belong to the session; layout, terrain image, frame blending and the
drag rectangle belong to the app; windows and renderers belong to the hosts.

---

## Replay page compositing — reverted 2 September 2026 (`532b797`)

Redrawing every wall each frame is wrong and compositing is the right approach.
The attempt rendered a blank canvas in the artifact viewer and was reverted
whole.

Leading hypothesis, untested: the old code floored cell size at 8 px; the
rewrite replaced that floor with one derived from `clientWidth`, which an
unlaid-out iframe reports as **0**.

Still open, and behind a larger question — the replay pages are a second
renderer, so every viewer feature gets written twice. Whether they become an app
or a server should be decided on which makes the better *instrument*, not which
is easier to share.
