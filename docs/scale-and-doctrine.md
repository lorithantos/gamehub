# Scale and doctrine

What a tick and a cell are worth, and how rank, retreat and repair got their
rules. Current code: `WorldScale`, `Combat`, `RepairPolicy`, `DemoWorld`,
`GuardDoctrine`, `PatrolDoctrine`.

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
