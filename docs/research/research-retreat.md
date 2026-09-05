# Retreat and disengage in shipped RTS — research notes (2026-09-04)

No game source was read. coh2.org was unreachable (ECONNREFUSED), Fandom returned 402, Relic's
patch archive 403, tauniverse 403, community.companyofheroes.com had a bad TLS cert. One GitHub
mod repo appeared in results and was skipped per the constraint.

## Lead finding

The exact failure we want to fix — a unit routing to base by the shortest path and dying to guns
along the way — is what Company of Heroes shipped, in three games, on purpose. Community pressure
to make retreat pick a safe route has run for a decade and Relic did not do it. The defence is
that unsafe routing is the *price* of retreat: "the point of it is that you cant spam 'retreat'
mindlessly. you need to have a thought about where your troops are going to go", and opponents
deliberately place MGs and Bofors on known retreat lanes — "You set up your troops to wipe enemy
squads, by forcing them to retreat through various dangers."
(https://steamcommunity.com/app/231430/discussions/0/3398435622554442095/ — community, medium
confidence; multiple corroborating threads.)

So the split we propose (get out of range first, then route by safety) removes CoH's main cost
control. If we take it, the cost has to be paid somewhere else.

## 1. Company of Heroes

What it does: pressing Retreat gives the whole squad a speed bonus, a large received-accuracy
bonus (harder to hit) and a damage reduction, and the squad becomes completely uncontrollable
until it reaches HQ, taking the shortest path.
(https://companyofheroes3.wiki/guides/cover-and-combat/,
https://steamcommunity.com/app/231430/discussions/0/2765630416816918715/ — wiki/community.)

Problem it solves: squads are persistent objects that carry veterancy, and reinforcing is cheaper
than rebuying, so the game is about preservation rather than disposal
(https://www.gamedeveloper.com/design/inspired-designs-in-relic-s-rts-games — design essay).
Blendo's Brendon Chung calls it "one of Company of Heroes' best design choices" and argues its
real product is narrative: you keep a squad long enough to care about it
(https://blendogames.com/news/post/2017-05-25-on-company-of-heroes/ — designer commentary, high
confidence as opinion).

Why it is structural: "Without the Retreat Button, it is arguable that the entire Company of
Heroes/Dawn of War combat system would entirely fall apart, as players would be unable to
disengage." The argument is that retreat makes engagement outcomes non-binary — partial loss
instead of wipe
(https://waywardstrategy.com/2016/02/17/rts-design-thought-control-of-tactical-outcomes/
— well-argued community design essay).

What it cost them: (a) retreat pathing is the single most complained-about thing in the series;
(b) Forward Retreat Points shorten the round trip so much that infantry behave as hit-and-run
units, which community analysis blames for blobbing — retreat lets you commit a deathball with
little punishment (https://www.coh2.org/topic/55569/why-blobbing-is-so-prominent-in-coh2 — cited
via search, site unreachable, low-medium confidence); (c) factions with shorter retreat distances
are structurally advantaged.

## 2. Trigger

CoH has no automatic retreat at all — it is always player-issued, and when players asked for an
auto-retreat toggle the community rejected it: "I think auto retreats would ruin a part of the
skill ceiling in this game."
(https://steamcommunity.com/app/231430/discussions/0/43099721587641399/)

Games that do fire it automatically use a morale/stress meter, not a health threshold:

- Total War: morale runs eager → steady → shaken → wavering → rout. Routing units are
  uncontrollable, flee by the most convenient route, and can rally; "shattered" units never
  return (https://totalwar.fandom.com/wiki/Morale via search; also
  https://shogun2-encyclopedia.com/how_to_play/052_enc_manual_battle_conflict_morale.html
  — official manual).
- Wargame: Red Dragon: Calm / Worried / Shaken / Panicked / Rout, tracked per unit; rate of fire
  and accuracy fall as it drops; a routed unit stops taking orders and "will flee in a straight
  line from an enemy, exposing it to fire from other enemy units"
  (https://wargame.fandom.com/wiki/Morale_and_Veterancy).
- Steel Division 2: Calm/Engaged/Worried/Stressed/Shaken/Panicked; panicked units are pinned or
  auto-retreat, vehicles fall back on their own, and infantry within ~100m surrender
  (https://steeldivision.fandom.com/wiki/Steel_Division_2_game_mechanics,
  https://guides.gamepressure.com/steel-division-2/guide.asp?ID=50848).
- Dawn of War 1: morale break costs speed, accuracy and damage but the squad does NOT flee — it
  stands in the open and dies; a mod (Fallback) added fleeing to the nearest strategic point
  (https://www.thegamer.com/warhammer-40k-dawn-of-war-morale-system-explained/,
  https://www.moddb.com/mods/fallback-mod). Relic dropped morale for suppression in DoW2 —
  suppressed units are slowed, take more damage, fire less — because it reads better.
- Stances (AoE2/RoN/AoM) do not implement retreat; they implement *chase leash*. Aggressive
  chases without limit; Defensive chases a fixed number of tiles then returns to its original
  position and backs off a few tiles from unseen ranged fire; Stand Ground does not move at all
  (https://ageofempires.fandom.com/wiki/Unit_stance via search — Fandom blocked direct fetch).
- Homeworld stances change combat stats, not withdrawal: Aggressive gives fighters +30% damage,
  +35% range and shot velocity; corvettes +25% damage, +30% range; strikecraft -20% fuel use.
  Evasive trades damage for survivability (https://homeworld.fandom.com/wiki/Tactics via search).

The pattern: auto-retreat is a *morale* verb in simulation-leaning games, and player-issued in
competitive ones. Nothing found used raw health as the trigger.

## 3. Exploit

- Fleeing pulls chasers. The Total Annihilation AI-writing community named it the "Pied-Piper
  bug": ground forces chase a single fleeing unit across the map until it dies or gets ~2 screens
  away, and fast units farm this (https://www.tauniverse.com/articles/tutorials/ai.html — 403 on
  fetch, quoted from search snippet, low-medium confidence). Our greedy step-away has the mirror
  risk: it makes our units the bait.
- Predictable routes get camped (CoH, above). Any deterministic disengage path is a place an
  opponent can pre-aim.
- Automatic panic makes the *shooter* the one in control. Steel Division 2 players argue a single
  mortar with zero casualties can paralyse an army and that offensives become impossible —
  "your whole army refuse to shot back or move what a joke"; the defence is realism
  (https://steamcommunity.com/app/919640/discussions/0/1648791520847223004/). This is the clearest
  published statement of the "too cheap to trigger" failure.
- The opposite failure is DoW1: break with no movement change, and units simply die anyway.
- CoH's cost model, for reference: retreat is free to issue but costs map presence, the round trip,
  and reinforcement manpower; the check is the route, not a cooldown.

## 4. Group retreat

Nothing directly on "line falls back together vs units peeling off". Adjacent findings:

- Total War manufactures group behaviour out of individual routs: a routing unit applies an area
  morale penalty to nearby friendlies, producing chain rout / cascade. In Medieval: Total War the
  stated penalty was up to -12 morale from nearby fleeing friendlies (-6 per full unit)
  (https://forums.totalwar.org/wiki/index.php/MTW_Morale,
  https://totalwarwarhammer.fandom.com/wiki/Leadership — wiki, medium confidence). Contagion is
  how a line "breaks together" without a group order.
- CoH retreat is per-squad but issued to a selection, so a group retreat is N individual retreats
  that each path independently — the source of the "my squad ran the wrong way" complaints.
- Group movement writing notes that in some situations "some or all units should collapse
  together, not maintain their offset from the group's center"
  (https://www.gamedeveloper.com/programming/group-pathfinding-movement-in-rts-style-games).

## 5. Numbers found

| Value | Game | Source | Confidence |
|---|---|---|---|
| Sniper retreat received-accuracy modifier 0.4 -> 0.65 | CoH2 Spring balance update | community.companyofheroes.com/discussion/244965 (TLS error on fetch; via search) | low-medium |
| "Speed bonuses and received accuracy bonus reduced from 50% to 35%" | CoH3 workshop balance changelog | steamcommunity.com/sharedfiles/filedetails/changelog/2994952108 (429 on fetch) | low; may not be the general retreat modifier |
| Retreat ~50% harder to hit (community estimate, varies by patch) | CoH2 | coh2.org threads via search (site down) | low |
| Watch closely below 50% squad health/models; retreat may be needed | CoH | gamereplays.org General Tip #76 | medium (advice, not data) |
| Aggressive: +30% damage, +35% range/shot velocity (fighters); +25%/+30% (corvettes); -20% fuel | Homeworld | homeworld.fandom.com/wiki/Tactics | medium |
| Neutral stance: 30% of missiles deliberately miss | Homeworld | same | medium |
| Morale regen +150% in light cover, ~200% in heavy | Dawn of War | thegamer.com | medium |
| Weaken Resolve: -90% morale regen for just under a minute | Dawn of War | thegamer.com | medium |
| Broken squads unbreak at 50% morale (25% for Space Marines) | Dawn of War | theropearrow.wordpress.com | medium |
| Surrender range ~100m when panicked | Steel Division 2 | gamepressure guide | medium |
| Nearby routing friendlies: up to -12 morale | Medieval: Total War | forums.totalwar.org wiki | medium |

## Could not establish

- Any first-party Relic statement of the retreat speed / received-accuracy / damage multipliers.
  Everything numeric is community or mod changelog.
- Any GDC talk or postmortem specifically on the retreat command. Nothing surfaced.
- Whether CoH3 retreat actually weights route safety. One fan wiki says "along a safer path"
  (companyofheroes3.wiki); Relic's patch archive returned 403 and no patch note confirming it was
  found. Treat as unverified.
- Any cooldown or lockout on retreat in any game.
- Any published design commentary on group-as-one-unit retreat versus individual peel-off, or on
  which reads better to a player. Genuine gap.
- Men of War / Gates of Hell morale specifics beyond "unless ordered to hold ground, will advance
  or retreat depending on morale" (rpgcodex forum, low confidence).
