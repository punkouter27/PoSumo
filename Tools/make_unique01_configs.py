"""Write Training/configs/<Name>Unique01.yaml for all 16 fighters.

Unique01 (2026-11-10): every fighter trains under its OWN behavior name, on its
OWN physique, with its OWN training recipe, for a five-minute corrective pass
after the shikiri-crouch fix (Agent_BipedBody.CROUCH_ANGLES). Each warm-starts
from its donor's *_intent01 checkpoint (the four trained fighters from their own).

"Unique" is real, not a label: each style changes how the policy is optimised
(learning rate, entropy, clip, discount, epochs, batch) and what it is trained
against (self-play mix, opening gap, random shoves, mat-width curriculum,
get-up share). shrink_start stays 0 for everyone: the game's mat does not shrink,
and a brain must not be fitted to an arena players never see.

Run: python Tools/make_unique01_configs.py
"""
from pathlib import Path

# name, donor, style label, lr, beta, epsilon, gamma, epochs, buffer, batch,
# play_against_latest, spawn_gap_half, shove_impulse, platform_difficulty, get_up_chance, note
STYLES = [
    ("Matt",    "Matt",    "baseline",        1.0e-4, 5.0e-3, 0.20, 0.997, 3, 10240, 1024, 0.25, 0.9, 0,  1.0, 0.3, "the reference recipe, shortened buffer so a 5-minute run gets several updates"),
    ("Grandma", "Grandma", "patient",         7.0e-5, 3.0e-3, 0.15, 0.998, 3, 10240, 1024, 0.25, 1.0, 0,  1.0, 0.3, "slow, conservative updates and a long horizon: wait for the opponent's mistake"),
    ("Nick",    "Nick",    "explorer",        1.5e-4, 1.0e-2, 0.20, 0.995, 3, 10240, 1024, 0.30, 1.1, 0,  1.0, 0.3, "high entropy, light and mobile: try new footwork from a wider opening gap"),
    ("Kim",     "Kim",     "rooted anchor",   1.0e-4, 4.0e-3, 0.20, 0.997, 3, 10240, 1024, 0.25, 0.9, 25, 1.0, 0.2, "random 25 N.s chest shoves: learn to stay planted under impact"),
    ("Alan",    "Matt",    "counter-puncher", 1.2e-4, 6.0e-3, 0.20, 0.996, 4, 10240, 1024, 0.50, 1.0, 0,  1.0, 0.3, "half its bouts against the LATEST self: answer the current best move"),
    ("Dupee",   "Matt",    "bulldozer",       1.0e-4, 3.0e-3, 0.25, 0.995, 3, 8192,  1024, 0.25, 0.7, 15, 1.0, 0.2, "close opening, wider clip: drive straight through the tachiai"),
    ("Porto",   "Matt",    "home-mat specialist", 1.0e-4, 5.0e-3, 0.20, 0.997, 3, 10240, 1024, 0.25, 0.9, 0,  0.0, 0.3, "platform_difficulty 0 = the full game-width mat every round: specialise on the arena players see"),
    ("Tong",    "Matt",    "quick learner",   2.0e-4, 5.0e-3, 0.15, 0.996, 2, 8192,  2048, 0.25, 0.9, 0,  1.0, 0.3, "high learning rate with fewer, larger epochs"),
    ("Brendan", "Grandma", "survivor",        8.0e-5, 4.0e-3, 0.20, 0.998, 3, 10240, 1024, 0.25, 1.0, 0,  1.0, 0.6, "60% of walk-lane episodes start on the clay: learn to get back up"),
    ("Maya",    "Grandma", "technician",      1.0e-4, 2.0e-3, 0.10, 0.997, 5, 10240, 512,  0.25, 0.9, 0,  1.0, 0.3, "tight clip, many small epochs: precise, low-variance updates"),
    ("Velez",   "Grandma", "heavy presser",   1.0e-4, 3.0e-3, 0.20, 0.998, 3, 12288, 1024, 0.25, 0.8, 20, 1.0, 0.2, "wide body under random shoves from a close start: lean and press"),
    ("Baily",   "Nick",    "dancer",          1.5e-4, 1.2e-2, 0.25, 0.994, 3, 8192,  1024, 0.30, 1.2, 0,  0.5, 0.3, "most exploration, widest opening, mat narrowed only half as far: footwork first"),
    ("Josh",    "Nick",    "opportunist",     1.2e-4, 7.0e-3, 0.20, 0.993, 3, 10240, 1024, 0.40, 1.0, 10, 1.0, 0.3, "short horizon and light shoves: take the chance that is there now"),
    ("Decoy",   "Kim",     "feint artist",    1.2e-4, 8.0e-3, 0.20, 0.996, 3, 10240, 1024, 0.60, 1.0, 0,  1.0, 0.3, "60% against the latest self: learn moves the current policy does not expect"),
    ("Manzi",   "Kim",     "wall",            6.0e-5, 3.0e-3, 0.15, 0.998, 3, 12288, 1024, 0.20, 0.9, 30, 1.0, 0.2, "strongest shoves, slowest learning: do not be moved"),
    ("Sokol",   "Kim",     "scrambler",       1.5e-4, 6.0e-3, 0.20, 0.995, 3, 8192,  1024, 0.30, 1.1, 10, 1.0, 0.5, "randomised narrow mats, shoves and frequent floor starts: recover and stay in"),
]

ROOT = Path(__file__).resolve().parent.parent
CONFIGS = ROOT / "Training" / "configs"


def config_for(row):
    (name, donor, style, lr, beta, eps, gamma, epochs, buffer, batch,
     latest, gap, shove, difficulty, getup, note) = row
    run = name.lower() + "_unique01"
    return f"""# {name} UNIQUE 01 - created 2026-11-10. Style: {style.upper()}.
#   {note}
#
# Five-minute corrective pass after the shikiri-crouch fix (hip -90 / knee 90 /
# spine 20,30,30): the old FK pose put the near thigh up to 10 cm inside the trunk
# for the whole frozen countdown. Every bout in both referees starts from this pose.
#
# Trains {name} under its OWN behavior name on its OWN sheet. Env: Builds/{donor}Env
# (SCN_TRAIN_{donor.upper()}) launched with `--env-args -posumoFighter {name}`
# (Systems_TrainingFighter swaps the character at load).
# Warm start: {donor.lower()}_intent01 checkpoint, staged as {name.lower()}_uniqueinit/{name}.
# Vector 53 obs / 14 actions, unchanged. --num-envs 2. Judge on ELO SHAPE from 1200.
#
# Launch: Training/Run-Unique01.ps1 (all 16, two at a time, 5 min each).

behaviors:
  {name}:
    trainer_type: ppo
    hyperparameters:
      batch_size: {batch}
      buffer_size: {buffer}
      learning_rate: {lr:.1e}
      beta: {beta:.1e}
      epsilon: {eps}
      lambd: 0.95
      num_epoch: {epochs}
      learning_rate_schedule: constant
    network_settings:
      normalize: true
      hidden_units: 512
      num_layers: 3
      vis_encode_type: simple
    reward_signals:
      extrinsic:
        gamma: {gamma}
        strength: 1.0
    keep_checkpoints: 3
    checkpoint_interval: 25000
    max_steps: 5000000   # never reached: the run is time-boxed to 5 minutes
    time_horizon: 1000
    summary_freq: 10000
    threaded: false
    self_play:
      window: 10
      play_against_latest_model_ratio: {latest}
      save_steps: 10000
      swap_steps: 10000
      team_change: 40000

environment_parameters:
  spawn_gap_half: {gap}
  shove_impulse: {shove}
  platform_difficulty: {difficulty}
  get_up_chance: {getup}
  shrink_start: 0.0
"""


def main():
    for row in STYLES:
        path = CONFIGS / f"{row[0]}Unique01.yaml"
        path.write_text(config_for(row), encoding="utf-8", newline="\n")
    donors = "\n".join(f"{r[0]},{r[1]},{r[2]}" for r in STYLES)
    (ROOT / "Training" / "unique01_roster.csv").write_text("fighter,donor,style\n" + donors + "\n", encoding="utf-8", newline="\n")
    print(f"wrote {len(STYLES)} configs")


if __name__ == "__main__":
    main()
