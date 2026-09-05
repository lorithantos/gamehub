# Fog of war: what shipped games remember and forget

## 1. Taxonomy

- **No fog / spotting only** (Wargame: Red Dragon). Buys readability and pure LOS play. Costs concealment as a resource. https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/WargameRedDragon
- **Shroud only, permanent reveal** (Total Annihilation, Supreme Commander: "no defogging"). Buys cheap exploration memory. Costs re-scouting tension. https://supcom.fandom.com/wiki/Intel
- **Two-layer shroud + fog; terrain and buildings remembered, units forgotten on sight loss** — the classic (Age of Empires, StarCraft II). Buys map knowledge plus permanent scouting pressure. Costs any modelling of enemy intent between scouts. https://www.jdxdev.com/blog/2022/06/08/rts-fog-of-war/ , https://petermcp.github.io/FOW-research/
- **Memory that can be wrong**: Age of Empires Online's "dim" state draws the fortress at the health you last saw; it may already be gone. Buys real uncertainty for free. Costs player trust in the display. https://ageofempiresonline.fandom.com/wiki/Fog_of_War
- **Detection layer with identity persistence** (Supreme Commander radar): a radar blip is anonymous; identity gained by vision is retained while radar keeps the track, and lost when the track is lost. Buys graded knowledge (something / what). Costs UI complexity. https://supcom.fandom.com/wiki/Radar
- **Per-unit relative spotting + C2 propagation** (Combat Mission CMx2) versus side-wide "borg spotting" (CMx1). Buys realistic ignorance and comms play. Costs comprehensibility and heavy bookkeeping. https://combatmission.fandom.com/wiki/Spotting (Fandom returned HTTP 402 to me; seen only via search extract)
- **Full persistent belief with uncertainty** (Command: Modern Operations contacts with Areas of Uncertainty; Command Ops 2 last-known intel reports). Buys stale-contact wargaming. Costs a whole second world model. https://command.matrixgames.com/?page_id=2711

## 2. Buildings remembered, units not

No designer talk or postmortem states the rationale outright — this is the weakest-sourced answer. What exists is dev-blog and wiki reasoning, consistent across sources: static things are *strategic* knowledge (map layout, base locations), mobile things are *tactical* uncertainty, and remembering them would remove the reason to scout continuously.

jdxdev states it as an explicit implementation rule: static obstacles are permanently revealed once seen; "only dynamic moving units are re-hidden in the fog", with fog justified as letting players "hide their tactics and unit manoeuvres". https://www.jdxdev.com/blog/2022/06/08/rts-fog-of-war/

FOW-research reaches the same split surveying AoE / SC2 / League of Legends. https://petermcp.github.io/FOW-research/

Wayward Strategy frames the underlying principle: "information revealed by an enemy unit is always actionable by a player's other units" — RTS information is side-wide and instantaneous, so remembered units would be immediately weaponisable rather than merely informative. https://waywardstrategy.com/2015/01/30/battlefield-uncertainty-and-fog-of-war/

A practical argument also appears in dev writing (not a cited designer statement): a remembered building can be targeted or queued against at its remembered spot and that is usually harmless; a remembered unit ghost would generate orders against something that is not there.

## 3. Forgetting by looking vs by timer

Shipped examples exist, and they mostly do **both**.

- **Combat Mission**: contacts are "possible but not certain" markers drawn with question marks; opacity is calibrated to certainty — "the darker it is the more recent, the more faded out it is the older/less accurate it is". Fading is the staleness channel. https://steamcommunity.com/app/1369370/discussions/0/4712410385461689618/
- **Command: Modern Operations**: a contact carries an Area of Uncertainty that **expands with time**; a submarine that reaches the last reported position of an uncertain contact and fails to find it "will begin searching within the AoU to try and narrow down the target location" — belief corrected by looking. There is also a hard timer: mobile facility and surface contacts have a 2-hour expiration age. Weapon-release logic keys off AoU size versus weapon tolerance. https://command.matrixgames.com/?page_id=2711
- **Command Ops 2**: no fog layer at all; enemies appear as intel reports and the last known location of a unit stays on the map. (Search-surfaced summary of the Steam guide; the guide page itself rate-limited on fetch.) https://steamcommunity.com/sharedfiles/filedetails/?id=1223954053
- Staleness is communicated by: opacity / fade (Combat Mission), a growing uncertainty ellipse (CMO), symbol ambiguity (question-mark icons), and message-log entries when a contact is reclassified.

## 4. What the AI is allowed to know

- **StarCraft II names its cheats in the difficulty list**: "Cheater 1: Vision" (AI sees your units), "Cheater 2: Resources", "Cheater 3: Insane". Vision is the first rung of cheating. https://starcraft.fandom.com/wiki/AI_script , https://liquipedia.net/starcraft2/Artificial_Intelligence ; players report Elite also behaving as if it sees through fog https://us.forums.blizzard.com/en/sc2/t/elite-ai-cheats-vision/2955
- **Soren Johnson (Civ IV lead designer), GDC 2008 "Playing to Lose"**: symmetrical games force the issue — "artificial intelligence often needs to cheat just to be able to compete with the player"; the original Civ let the AI create units for free under fog, which "clearly showed how the computer was playing by different rules from the human". His conclusion is about perception: "when the question is one of fairness, the player is always right." https://www.gamedeveloper.com/game-platforms/analysis-game-ai-our-cheatin-hearts , https://archive.org/details/GDC2008Johnson2
- **Halo Wars 2** is the public counter-example: designers "intended for Atriox and his commanders to exhibit strategic play without resorting to cheating", against a genre norm of "enemies that ignore the fog of war and always know where you are". The article does not say how it models enemy positions. https://www.aiandgames.com/p/how-commander-ai-works-in-halo-wars
- **Age of Empires IV**: hardest difficulty admitted resource cheating and was revised after complaints; forum discussion only, no vision-cheat statement. https://forums.ageofempires.com/t/how-does-the-a-i-cheat-in-aoe4/179666
- **Academic (labelled)**: Weber, Mateas & Jhala, "A Particle Model for State Estimation in Real-Time Strategy Games", AIIDE 2011 — a particle filter over previously-seen enemy units, parameters learned by mining expert StarCraft replays, ~10% bot improvement. This is the honest-belief alternative to vision cheating. https://ojs.aaai.org/index.php/AIIDE/article/view/12424 . Also DefogGAN (AAAI 2020), predicting hidden state from partial observation https://arxiv.org/pdf/2003.01927 , and the thesis "Dealing with Fog of War in a Real Time Strategy Game Environment" (diva2:835962), which I could not fetch (connection refused).

## 5. Fog as an instrument problem

- **GDC 2014 AI Summit, "Out of Sight, Out of Mind: Improving Visualization of AI Info"** — three studios on exposing *why* (and why not) an agent decided as it did: Turtle Rock's timeline view for Evolve, The Sims 4 HTTP-based Game State Inspector, Guerrilla's ReView for the Killzone: Shadow Fall bots. Slides are public. https://www.gdcvault.com/play/1020590/Out-of-Sight-Out-of , https://www.guerrilla-games.com/read/out-of-sight-out-of-mind-improving-visualization-of-ai-info
- Nothing found that is specifically a *per-side knowledge* viewer for a whole match. The nearest shipped things are replay observer modes with per-player vision; the nearest published tool is academic: "An Approach to Interactive Analysis of StarCraft: BroodWar Replay Data" (Springer, 2021). https://link.springer.com/chapter/10.1007/978-3-030-70296-0_20

## Could not establish

- No designer statement (talk, postmortem, interview) explaining the buildings-remembered / units-forgotten asymmetry. Only dev blogs and wikis argue it.
- No shipped RTS found that keeps remembered enemy **unit** ghosts cleared *only* by observation with no timer; every persistent-contact game located also ages or expires contacts.
- Could not verify Combat Mission's exact rule for clearing a contact by looking (Fandom wiki HTTP 402, Battlefront forum HTTP 403).
- No public postmortem on debugging an AI that acts on partial knowledge, as distinct from general AI debug tooling.
- **Skipped as source code** per the constraint: the eisbot repository (github.com/bgweber/eisbot) appeared in results and was not opened; the AIIDE paper was used instead.
