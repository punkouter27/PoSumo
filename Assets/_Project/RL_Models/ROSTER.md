# PoSumo Fighter Roster (16 trained fighters)

> **Update 2026-11-22 (tournament standard).** The twelve fighters below that started as
> placeholders were each promoted to their own trained brain in the Unique01 pass (2026-11-11;
> see each `MANIFEST.md`), and `borrowedBrainFrom` is empty on every asset. They are real,
> playable fighters and stay in the roster; a **BYE** is only an empty slot, which a full
> 16-fighter roster never has. The draw is seeded by tournament Elo (1v16, 8v9, 4v13, 5v12,
> 2v15, 7v10, 3v14, 6v11), no longer shuffled. The sections below are the 2026-11-10 history.

**Since 2026-11-10 the bracket is 16 slots**, drawn as a tree: eight seeds down the left,
eight down the right, the final and champion in the centre. Four fighters are RL-trained;
the other twelve are **PLACEHOLDERS** that fight on a **borrowed** brain until they are
trained (`borrowedBrainFrom` on the character asset; the debug panel says PLACEHOLDER).
They are still RL fighters — there is no heuristic fighter (removed 2026-10-04).

There is no code mirror of this table — each fighter's `<Name>_Character.asset` is the
single source of truth, and `Systems_TournamentBracket._roster` (SCN_TOURNAMENT) is where
the entrants are assigned. *PoSumo → Generate Placeholder Fighters* rebuilds the twelve
and re-wires that roster.

| Fighter | Behavior | Folder | Brain |
|---|---|---|---|
| MATT | `Matt` | Matt_v01 | own (`*_lunge01`) |
| GRANDMA | `Grandma` | Grandma_v01 | own |
| NICK | `Nick` | Nick_v01 | own |
| KIM | `Kim` | Kim_v01 | own |
| Tong, Alan, Josh | same | `<Name>_v01` | borrows **Matt** |
| Maya, Baily, Porto | same | `<Name>_v01` | borrows **Grandma** |
| Brendan, Decoy, Sokol | same | `<Name>_v01` | borrows **Nick** |
| Dupee, Velez, Manzi | same | `<Name>_v01` | borrows **Kim** |

Placeholder bodies are the donor's at ±4% mass / width — kept small because the borrowed
policy was fitted to the donor's physique.

## Faces and voices (2026-11-10)

Both use four moods — **Happy, Angry, Hurt, Insult** — at levels **1-5** (1 mildest), plus a
Neutral face. *(2026-11-22: a fifth, **Celebrate**, completes the standard 5 x 5 matrix; it fires
on winning a round (2-4) or the match (5). Standard voice files are
`Resources/Audio/Voice/<Name>/<emotion>-<level>.wav`, checked before the legacy names below; a
fighter with no celebrate face art shows its Happy art.)* `Systems_FighterVoice` decides every emotional moment and announces it on
`Expressed`; `Systems_FaceMood` draws the same mood and level.

| | Real asset (wins when present) | Fallback |
|---|---|---|
| Face | `Assets/_Project/Environments/Art/Faces/<Name>_<Mood>_<1-5>.png` | nearest level of that mood, else `<Name>_Neutral` + procedural tint/squash/tilt |
| Voice | `Resources/Audio/Voice/<Name>_<Mood>_L<1-5>_<1-5>.wav` (5 variants per level) | synthesised at runtime by `Systems_VoiceSynth` |

- Trained four: real face photos at Happy 1/3/5 and Hurt 1/3/5 (the old Happy 1-3 / Sad 1-3);
  real voice lines as variant 1 of Happy / Hurt (was Sad) / Insult at every level. Angry is
  fully procedural.
- Placeholders: one FAKE right-facing profile Neutral each (cut from a front photo by
  `scripts/make_placeholder_faces.py` — a true side view needs a generative model), every
  expression and every voice line procedural.

## Clothing (visual only)

Each `<Name>_Character.asset` carries a `clothing` block — garment plus colour per
body region. The fabric pattern itself is generated in code by `Agent_Fabric`
(denim twill, fleece, wool, canvas…) so no fighter ships a texture asset, and
`Agent_BipedBody.PartColor` decides which part each garment covers. Clothing
touches no mass, no collider and no observation, so it cannot invalidate a brain.

| Fighter | Legs | Torso | Arms | Feet |
|---|---|---|---|---|
| Matt | jeans (indigo) | white tee | — | white sneakers |
| Grandma (formerly Standard) | grey sweatpants | beige knit sweater | long sleeves | cream socks |
| Nick | navy shorts | light tank | — | light sneakers |
| Kim | dark mawashi | — (bare chest) | — | — (barefoot) |

Physique is mass / width / torque scale. Each folder's `MANIFEST.md` holds the
full spec and the exact retrain command.

Colour names above are descriptions of `teamColor` on the character asset, which is
the authority — this table said "teal" for Kim while her asset has been purple
(0.62, 0.32, 0.62). Read the asset, not this column, if the two ever disagree again.

## Adding a fighter

1. Duplicate a `*_Character.asset`, rename it `<Name>_Character.asset` in a new
   `Assets/_Project/RL_Models/<Name>_v01/` folder, and set `behaviorName` to `<Name>`.
2. Tune the build scales and reward shaping on the asset — style lives in data,
   never in `Agent_Biped`.
3. Copy a config to `Training/configs/<Name>Unified01.yaml`, change the
   `behaviors:` key to `<Name>`, and record *why* its hyperparameters differ in
   the header comment (that comment is the project's training log).
4. Duplicate a training scene as `SCN_TRAIN_<NAME>`, point its
   `Systems_MatchRoster` at the new character, add a `PoSumo/Build <Name>
   Training Env` entry building to `Builds/<Name>Env/<Name>Env.exe`, build, train.
   **Verify the assignment by reading the saved `.unity` file**, not the wiring
   script's log — a pass once reported success while the scene on disk still held
   `character: {fileID: 0}`, and the env trained the wrong policy for 1.5M steps.
5. Deploy with a `PoSumo/Deploy <Name> Brain` entry, then add the character to
   `Systems_TournamentBracket._roster` in `SCN_TOURNAMENT`.

The **45**-observation / 13-action contract must stay identical across all
fighters so any pair can share an arena. That is 42 base plus the 3 added by
`extendedObservations`, which every shipped fighter has on;
`Agent_Biped.ObservationCount` is the authority, not this line.
