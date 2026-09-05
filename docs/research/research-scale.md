# Spatial and temporal scale in shipped RTS games

Confidence: **H** = stated by developer/publisher; **M** = community wiki with stated data-mining or reproducible testing; **L** = forum testimony only.
No game source code was read. Liquipedia and Fandom tables below are data-mined community wikis and are labelled as such.

## The table

| Game | Sim tick rate | Metres per cell | Unit speed | Source |
|---|---|---|---|---|
| **Age of Empires I/II** | Communications turn **~200 ms** (5/s), variable — host retunes turn length live | not stated | **0.5–7.5 tiles per in-game second**, engine-clamped; Villager 0.92, Cavalry 1.66, Catapult 0.66 (AoE1) | tick **H** [gamedeveloper](https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond); speed **M** [AoE wiki, Genie Engine](https://ageofempires.fandom.com/wiki/Speed) |
| **StarCraft / Brood War** | **42 ms per Logical Step at Fastest = 23.81/s**; Normal 67 ms = 14.93/s; Slowest 167 ms = 5.99/s | not stated; grid is **32 px build tile**, **8 px walk tile** | raw speed in **px per Logical Step**, fractional: Marine 4.00, Vulture 6.40, Zergling 5.49, Interceptor 13.33. Marine ≈ 95 px/s ≈ **2.98 build-tiles/s** at Fastest (derived) | tick+speed **M** [Liquipedia BW Game Speed](https://liquipedia.net/starcraft/Game_Speed) (cites starcraftai.com + BWAPI docs); grid **M** [BWAPI guide](https://makingcomputerdothings.com/brood-war-api-the-comprehensive-guide-of-time-and-space/) |
| **StarCraft II** | **1 game loop = 0.0625 game seconds → 16/game-second**; Faster = ×1.4 → **22.4 loops/real second ≈ 44.6 ms** (derived) | not stated — speed is in "game distance units", same units as Range | Marine/Zealot/Hydralisk **3.15**, Stalker 4.13, Zergling 4.13, Overlord 0.902 units/s; fractional | loop **H** [Blizzard 4.13.0 patch notes](https://news.blizzard.com/en-gb/starcraft2/23471116/starcraft-ii-4-13-0-ptr-patch-notes); factors **M** [Liquipedia SC2 Game Speed](https://liquipedia.net/starcraft2/Game_Speed); speeds **M** [Liquipedia SC2 Speed](https://liquipedia.net/starcraft2/Speed) |
| **Supreme Commander** | **Fixed 10 fps sim, 100 ms per SimTick**, all the time; render layer separate at 60 fps | map cells: **256×256 = 5×5 km → ~19.5 m/cell** (512=10 km, 1024=20 km, 2048=40 km, 4096=81 km) | not established | tick **H** (Forrest Smith, ex-GPG) [gamedeveloper](https://www.gamedeveloper.com/design/opinion-synchronous-rts-engines-and-a-tale-of-desyncs) / [original](https://www.forrestthewoods.com/blog/synchronous_rts_engines_and_a_tale_of_desyncs/); map **M** [SupCom wiki](https://supcom.fandom.com/wiki/Map) |
| **Age of Empires IV** | not established | **1 tile = 4 metres**, explicit: range is authored in metres in the editor and shown in tiles in the UI, so ranges come in 0.25-tile steps | **tiles/second**, hard cap **2.00 tiles/s = 8 m/s** regardless of buffs. Horseman 1.88, heavy cav 1.62, infantry 1.12–1.38, War Elephant 1.0, siege 0.75–0.88 | **M** [AoE wiki Range](https://ageofempires.fandom.com/wiki/Range), [AoE wiki Speed](https://ageofempires.fandom.com/wiki/Speed); cap corroborated by player timing test + dev acknowledgement [AoE forums](https://forums.ageofempires.com/t/movement-speed-is-it-intended-for-it-to-be-limited-to-about-2-00-tiles-per-second-in-game-despite-what-the-unit-card-says/207305) |
| **Warcraft III** | disputed: community testing gives **~45–50 ticks/s** at 1.0× speed; subsystems differ (rotation every 0.03 s, attack cooldown every 0.02 s). Slow = 0.8×, Slowest = 0.6× | not established | not established | **L** [Hive Workshop testing thread](https://www.hiveworkshop.com/threads/using-game-events-as-a-clock-source-instead-of-timers.338408/) |
| **Company of Heroes / Total War** | **not established** | not established | not established | — |

## Tick rate and command latency

The lockstep bargain, stated plainly in *1500 Archers*: only commands cross the wire, so unit count is free, but every machine must reach the same state on the same turn. Ensemble scheduled commands issued on turn 1000 for execution on **turn 1002** — a two-turn delay — so the packets had a whole turn to arrive. Turns were **~200 ms**, giving ~400 ms command latency. They had measured the tolerance first: **under 250 ms latency goes unnoticed, 250–500 ms is highly playable, past 500 ms players notice**. The key finding was about variance, not magnitude — *"a consistent slower response was better than alternating between fast and slow command latency."* So turn length was not fixed: each client reported its sustainable frame rate and its ping, and the host broadcast a new target frame rate and communications turn length, weighted to **rise quickly and settle back slowly**. The target floor was 15 fps on a Pentium 90 over a 28.8 modem.

Supreme Commander shows the same arithmetic with a fixed tick. Smith: *"Each SimTick needs to run within 100ms, or the game will play in slow motion."* Single-player click-to-response is therefore **0–100 ms**; multiplayer sends the order for a SimTick two ahead, **200–300 ms**. That is the direct cost of a 10 Hz sim — and the direct purchase is *"thousands of units"* over eight-player internet play.

Note the two games sit at opposite ends: AoE adapts turn length to the network, SupCom pins the sim and lets the frame rate float. StarCraft pins nothing to the network at all in this data — its "game speed" is a wait between logical steps, and Fastest (42 ms) is what everyone actually plays.

## Consequences designers stated

- **Fine grid under a coarse one.** Brood War kept two grids: a **32×32 px tile** for placing buildings and an **8×8 px walk tile** for walkability. The build grid was too coarse to path on.
- **Quantised range as a visible artefact.** AoE4 authoring range in metres against a 4 m tile means ranges can only land on 0.25-tile increments; the UI rounds, but the rounding is *"purely visual"*.
- **A speed cap the UI lies about.** AoE4 caps movement at 2.00 tiles/s even when the unit card claims 3.06. A player measured it over a 10-second race; a dev acknowledged the report. Whatever the reason, it means the displayed number is not the simulated number.
- **Scale is chosen for readability, not realism.** Dustin Browder on the Ultralisk: making it bigger *"would look a lot better"*, but it would hide the ~20 zerglings behind it and destroy readability. Same logic moved the Terran healer from infantry to a mid-sized ship so opponents could read it. ([GDC 2011 coverage](https://www.gamedeveloper.com/game-platforms/gdc-2011-developing-i-starcraft-ii-i-like-inventing-basketball-2-), [The Design of StarCraft II](https://www.gamedeveloper.com/business/the-design-of-i-starcraft-ii-i-))
- **SC2 changed its own clock unit publicly.** Before Legacy of the Void all displayed times were Normal-speed game seconds, so a MULE tooltip said 90 s but lasted 65 s real. LotV switched every displayed duration to real seconds. Internally and in the editor, everything is still Normal speed. That is the closest thing found to a public regret about a temporal-unit choice.
- **SupCom's stated map scale does not obviously survive contact with unit size.** A 5×5 km map is 256×256 cells, i.e. ~19.5 m per cell. I did not find a sourced unit footprint to pair with it, so the "tank the size of a house" comparison is not established here.

## Not established

- **Company of Heroes / Dawn of War (Essence engine) tick rate.** Nothing published found. Forrest Smith worked at Relic but the article found discusses Supreme Commander only.
- **Total War battle simulation rate.** Nothing found in AMD GPUOpen's four-part "Anatomy of the Total War Engine" search results or CA material; those pieces are rendering-focused.
- **Metres per cell for StarCraft, StarCraft II, Warcraft III, Age of Empires I/II, Company of Heroes.** None of these publish a metric conversion. SC2's own term is "game distance units". AoE4 is the only game in this set that states one.
- **Warcraft III tick rate authoritatively.** Community measurements disagree (33.3, 45, 50 per second) and are inferred from attack-rate tests, not from any developer statement.
- **Whether any of these speeds are integers.** Every published table found is fractional (BW px/step to 2 dp, AoE tiles/s to 2 dp, SC2 to 3 dp). No game found publishes integer speeds.
- **Any public developer regret about tick rate or grid coarseness.** Not found. The SC2 clock-unit change is a change, not a stated regret.

## Skipped

No source repositories were opened. Where results pointed at FAForever's Lua repository or similar, they were skipped; the Supreme Commander tick rate here comes from a developer blog post, not from code.
