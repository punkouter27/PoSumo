using UnityEngine;

namespace PoSumo
{
    /// One asset per fighter: identity, body build, brain generation, and all
    /// sumo reward-shaping coefficients. Agents copy from this at Awake when
    /// assigned (same pattern as Systems_GameTuning), so creating or tuning a
    /// character means editing an asset — never code.
    ///
    /// Training a new character: duplicate an asset, tweak, duplicate a YAML
    /// with the matching behavior name, and point the training env builder at
    /// the asset.
    [CreateAssetMenu(fileName = "CharacterDefinition", menuName = "PoSumo/Character Definition")]
    public sealed class Agent_CharacterDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("ML-Agents behavior name; must exactly match the YAML config key.")]
        public string behaviorName = "Matt";
        public Color teamColor = new Color(0.85f, 0.25f, 0.2f);
        [Tooltip("Face texture for the head; null = plain circle.")]
        public Sprite headSprite;

        [Header("Face")]
        [Tooltip("Tick when the source art faces LEFT. Rig geometry is right-facing, so the head flip is inverted for this character.")]
        public bool faceArtFacesLeft = false;
        [Tooltip("Prefix of this fighter's face art in the FaceAtlas; empty = behaviorName.\n\nFaces resolve by convention: <prefix>_Neutral, then <prefix>_<Happy|Angry|Hurt|Insult>_<1-5>. Any missing level uses the nearest level of the same mood; a missing mood is drawn from Neutral with a procedural tint, squash and head tilt (Systems_FaceMood). Drop a real PNG at the exact name and it wins, one slot at a time.\n\nReplaced faceNeutralName / faceHappyNames / faceSadNames on 2026-11-10.")]
        public string faceArtName = "";

        [Header("Voice")]
        [Tooltip("Glottal fundamental (Hz) of this fighter's SYNTHESISED voice lines. 0 = derived from the behavior name. Only used for lines with no recorded WAV.")]
        public float voicePitchHz = 0f;
        [Tooltip("Vocal-tract length factor of the synthesised voice: >1 = longer tract = lower formants (a bigger body), <1 = smaller.")]
        public float voiceTract = 1f;

        [Header("Placeholder")]
        [Tooltip("Behavior name of the trained fighter whose brain this one BORROWS; empty = its own brain.\n\nA placeholder fighter (added 2026-11-10 to fill the 16-slot bracket) has an identity, face, voice and a slightly altered body, but no training of its own: inferenceModel is the donor's .onnx and every brain-generation field matches the donor's, or the model would be fed a vector it cannot accept. Shown as PLACEHOLDER in the debug panel. Clear it when the fighter gets its own trained brain.")]
        public string borrowedBrainFrom = "";

        /// The face-art prefix this fighter resolves under.
        public string FaceArtPrefix => string.IsNullOrEmpty(faceArtName) ? behaviorName : faceArtName;

        /// True while this fighter fights on another fighter's brain.
        public bool IsPlaceholder => !string.IsNullOrEmpty(borrowedBrainFrom);

        [Header("Clothing (visual only)")]
        [Tooltip("What this fighter wears, per body region: garment plus colour. The fabric pattern comes from the garment itself (jeans are denim, sweatpants are fleece) and is generated in code by Agent_Fabric — no texture assets, nothing to import.\n\nVisual only: it changes a part's texture and renderer colour and nothing else — no mass, no collider, no joint, no observation — so it cannot invalidate a trained brain. Coverage is decided in Agent_BipedBody.PartColor; the head is never clothed.")]
        public Agent_ClothingSpec clothing = new Agent_ClothingSpec();

        [Header("Body build")]
        [Tooltip("Multiplies every part's mass. Heavyweight sumo ~2.")]
        public float massScale = 1f;
        [Tooltip("Widens the four torso segments (sumo belly). Heavyweight ~1.3.")]
        public float widthScale = 1f;
        [Tooltip("Multiplies joint motor torque caps to carry the extra mass.")]
        public float torqueScale = 1f;

        [Header("Brain generation")]
        [Tooltip("44-obs brain (+opponent uprightness/down/edge). Must match the model's input size. Standard for all new characters; only legacy brains use 41 obs.")]
        public bool extendedObservations = true;
        [Tooltip("DecisionRequester period the model was trained at. Standard is 3; legacy brains used 5.")]
        public int decisionPeriod = 3;
        [Tooltip("Adds 4 observations: per-foot ground contact and the share of body weight carried through each foot.\n\nOFF for every shipped brain, and it must stay off until that fighter is retrained — it changes the observation vector length, so an existing .onnx would be fed a vector its input layer cannot accept.\n\nWorth turning on for the NEXT training run: without it the policy cannot tell a planted foot from one in the air, or feel how hard it is loading the mat, which is the single most important thing a wrestler senses.")]
        public bool contactObservations = false;
        [Tooltip("Adds 1 observation: whole-body stamina, 1 = fresh down to 0 = fully fatigued.\n\nOFF for every shipped brain for the same reason contactObservations is — it lengthens the observation vector, so an existing .onnx cannot be fed it.\n\nTurn it on for the NEXT run, together with the corrective pass the fatigue model requires anyway. Joint torque now decays with accumulated work whether or not this is on; leaving it off means the policy is fighting a body that is silently getting weaker with no way to perceive it, so it cannot learn to pace itself or to time a push against a tiring opponent.")]
        public bool staminaObservation = false;
        [Tooltip("Adds 2 observations, appended last: the opponent's lunge intent and remaining lunge cooldown. Changes the vector length (51 -> 53) — must match the model. A placeholder must carry its donor's value.")]
        public bool intentObservations = false;
        [Tooltip("Adds 2 observations, appended LAST (after intent): the body's centre of mass and its capture point (Hof's extrapolated CoM), each relative to the middle of the two feet, facing-mirrored, in metres / 0.5. Changes the vector length (53 -> 55) — must match the model. A placeholder must carry its donor's value. Training envs can force it on with -posumoBalanceObs (Systems_TrainingFlags), so the sheet stays off until the retrained brain is deployed.")]
        public bool balanceObservations = false;
        [Tooltip("Trained model for inference playback.")]
        public Unity.InferenceEngine.ModelAsset inferenceModel;

        [Header("Sumo reward shaping (training only)")]
        public float uprightReward = 0.0005f;
        [Tooltip("Per-step reward for closing speed toward the opponent (gated by stance).")]
        public float closingReward = 0.0006f;
        [Tooltip("Extra reward per m/s of closing speed above lungeThreshold.")]
        public float lungeBonus = 0.001f;
        public float lungeThreshold = 1.5f;
        [Tooltip("Reward per m/s of relative impact speed delivered into the opponent.")]
        public float impactReward = 0.01f;
        public float impactCap = 8f;
        public float kneeBendReward = 0.0004f;
        public float hipsLowReward = 0.0003f;
        [Tooltip("Bonus per alternating-foot step while standing.")]
        public float cadenceReward = 0.0015f;
        [Tooltip("Reward per meter of torso rise while down (getting back up).")]
        public float riseReward = 0.02f;
        [Tooltip("UNGATED per-step cost of squared motor effort. The older energyPenalty is switched off whenever the fighter is moving fast, which made full-power flailing free mid-charge and left 7-12 of the 13 motors railed. This one always applies and is quadratic, so it bites hardest exactly at the rails. 0 restores the old behaviour.")]
        public float effortPenalty = 0.0015f;
        [Tooltip("Per-step reward for a real sumo base: both feet planted, planted apart, and knees bent. Nothing rewarded stance before, and fighters were measured mid-bout standing on one foot with the chest 0.87 m off the mat.")]
        public float stanceReward = 0.0009f;
        [Tooltip("Per-step reward for driving the opponent back while BOTH feet carry load, scaled by the weaker foot's share of body weight. Requires contactObservations to be meaningful.\n\n0 by default because every shipped brain trained without it. Sumo is won by sustained push, but every other term here pays for a collision, so policies learned to strike rather than drive. Try 0.004-0.008 on the next run.")]
        public float driveReward = 0f;
        [Tooltip("Per-step reward for holding ground at the rim while wrestled there: within 0.4 m of the edge behind, opponent in range, both feet planted and not sliding, torso not retreating. 0 = off (every brain before 2026-11-10). Keep it small: 0.0006 pays 0.3 for a full 10 s stand against a -1 ring-out.")]
        public float edgeBraceReward = 0f;
        public float energyPenalty = 0.0004f;
        public float jerkPenalty = 0.0003f;
        [Tooltip("Per-step cost of a LOADED foot sliding: sum over feet of (share of body weight on that foot) x |foot speed along the mat|, m/s. A swing foot carries no load and is free; a planted foot skating under the body is charged. 0 = off (every brain before 2026-11-10). A training run can override it with the slip_penalty environment parameter.")]
        public float slipPenalty = 0f;
        [Tooltip("Per-step cost of the SECOND difference of the motor commands, mean |a(t) - 2a(t-1) + a(t-2)|. jerkPenalty charges how much a command changes; this charges how much the change changes, which is what a buzzing, alternating command looks like. 0 = off. Override: jerk2_penalty environment parameter.")]
        public float jerk2Penalty = 0f;
        [Range(0f, 1f)]
        [Tooltip("Fraction of movement rewards earned with straight legs; deep stance earns 100%.")]
        public float straightLegEarnFraction = 0.3f;

        [Header("Walk school shaping (training only)")]
        // Defaults are exactly the constants the Mode.Walk branch used before these
        // fields existed, so an untouched character trains the identical gait it
        // always did. Only deviate deliberately.
        [Tooltip("Reward per m/s of forward speed toward the target, gated by stance.")]
        public float walkForwardReward = 0.004f;
        [Range(0f, 1f)]
        [Tooltip("Fraction of the forward reward earned while CRAWLING; walking at full standing height earns 100%. Low = must stand up to be paid. NOTE this gate was on knee bend until 2026-08-08 and meant the opposite — it paid for a deep crouch, which is exactly what produced the crawl. See Reward_WalkObjective.Evaluate.")]
        public float walkStanceFloor = 0.15f;
        [Tooltip("Per-step reward for knee bend while walking.")]
        public float walkBendReward = 0.0006f;
        [Tooltip("Per-step reward for an upright chest while walking. This is chest ORIENTATION only — a deep crouch with a vertical chest scores a full 1.0 here, which is why it cannot stop a crawl on its own. Use walkHeightReward for that.")]
        public float walkUprightReward = 0.001f;
        [Tooltip("Per-step reward for carrying the torso at standing height (0.95 m above the mat) rather than crouched (0.65 m). THIS is the anti-crawl term: walkUprightReward measures which way the chest points, this measures how far off the mat it is. 0 disables it and restores the pre-2026-08-07 gait.")]
        public float walkHeightReward = 0f;
        [Tooltip("Bonus per alternating-foot step. High = dances, low = drives.")]
        public float walkCadenceReward = 0.002f;
        [Tooltip("Per-step cost of motor effort while walking.")]
        public float walkEnergyPenalty = 0.0003f;
        [Tooltip("Per-step penalty for standing still — stops the walker farming upright rewards as a statue.")]
        public float walkStallPenalty = 0.0008f;
        [Tooltip("Walk-school copy of slipPenalty: charges a loaded foot that slides. 0 = off. Override: walk_slip_penalty environment parameter.")]
        public float walkSlipPenalty = 0f;
        [Tooltip("Walk-school copy of jerk2Penalty. 0 = off. Override: walk_jerk2_penalty environment parameter.")]
        public float walkJerk2Penalty = 0f;
    }
}
