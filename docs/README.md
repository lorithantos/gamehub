# Design record

What the code used to do, what was tried, and what the numbers were when a
decision was taken. The code says what it does now; this says how it got there.

## Why the split

A doc comment is a tooltip. It is read by someone hovering a member while
holding a different problem in their head, and it has that person's attention
for about two lines. Anything longer competes with the code for the same
attention and usually loses — the result is a member whose comment is skipped
wholesale, which is worse than a short one that is merely incomplete.

The line that decides where a sentence goes is **tense**:

| Tense | Where it goes | Why |
|---|---|---|
| Present — what this does, what a caller can get wrong | the doc comment | has to track the code, so it belongs next to it |
| Past — what it replaced, what was tried, what was measured | this directory | cannot rot; it was true on the day and stays true |

The property that makes this worth doing: **a past-tense claim needs no
maintenance.** "On a 512×384 map that is 62% wall it found nothing at all"
is permanently true of the code that existed on 2 September 2026. Move it here
and it is finished. Leave it in a comment and it sits in the tooltip forever,
describing a class that no longer exists, for a reader who wanted to know what
the one in front of them does.

The present-tense half is then small enough to be checked, and is checked —
`Directory.Build.props` turns on XML doc generation, so a `cref` pointing at a
renamed type or a `param` naming an argument that has gone is a build warning
rather than prose that happens to sit above a member.

## Conventions

- **One file per topic**, named for the thing rather than the date.
- **Newest first** within a file, each entry dated.
- **Numbers keep their conditions**: the map, the machine, the commit if it
  matters. A figure without them is not a measurement, it is a rumour.
- **Referenced from the code** it explains, by path, in the remarks it was cut
  from — and registered in the janet catalog as a `file.*` node so it is
  retrievable without knowing it exists.

## Files

| File | Covers |
|---|---|
| [gates-and-regions.md](gates-and-regions.md) | chokepoint detection, the region partition, routing over it |
| [search-and-movement.md](search-and-movement.md) | the flat search, the workspace, group movement and field following |
| [scale-and-doctrine.md](scale-and-doctrine.md) | world calibration, rank, retreat, the repair reserve |
| [viewer.md](viewer.md) | the viewer as an instrument, and what it could not show |
