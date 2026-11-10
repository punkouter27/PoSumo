// `#if UNITY_EDITOR` is redundant in Assets/Editor/; it is here only because
// .claude/hooks/guard-editor-runtime.sh matches the Editor folder with forward
// slashes and is handed a Windows path. Harmless.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoSumo.EditorTools
{
    /// *PoSumo -> Generate Placeholder Fighters*: fills the 16-slot bracket.
    ///
    /// The user's brief (2026-11-10): "In the main menu have placeholders for 16
    /// players ... I will eventually have 16 players ready and trained." Four are
    /// trained (Matt, Grandma, Nick, Kim). This creates the other twelve as
    /// PLACEHOLDER fighters, each with:
    ///
    ///   * its own identity — behavior name, team colour, face, voice;
    ///   * a BORROWED brain — the donor's .onnx and every brain-generation field
    ///     copied from the donor asset, because the model must be fed exactly the
    ///     vector it was trained on (`borrowedBrainFrom` records whose);
    ///   * a small body change (+-4% mass / width) so it is not a carbon copy, kept
    ///     small because the borrowed policy was fitted to the donor's physique.
    ///
    /// They are RL fighters, so the "no heuristic fighter" rule (CLAUDE.md,
    /// 2026-10-04) holds. To promote one to a real fighter: train it, deploy its
    /// own .onnx to its folder, and clear `borrowedBrainFrom`.
    ///
    /// Idempotent: re-running updates the existing assets in place (GUIDs kept)
    /// and re-wires SCN_TOURNAMENT's roster. Also:
    ///   * sets the import settings the FaceAtlas needs on every face PNG
    ///     (Sprite, uncompressed — a compressed source loses detail before packing);
    ///   * gives the four trained fighters the voice identities the old WAV
    ///     generator used, if they have none yet.
    ///
    /// Face art comes from `python Tools/make_placeholder_faces.py` (run first).
    public static class GeneratePlaceholderFighters
    {
        private const string AGENTS_DIR = "Assets/Agents";
        private const string FACES_DIR = "Assets/Art/Faces";
        private const string TOURNAMENT_SCENE = "Assets/Scenes/SCN_TOURNAMENT.unity";
        private const string ROSTER_FIELD = "_roster";

        private static readonly string[] Trained = { "Matt", "Grandma", "Nick", "Kim" };

        private readonly struct Spec
        {
            public readonly string Name;
            public readonly string Donor;
            public readonly Color Colour;
            public readonly float MassTweak;
            public readonly float WidthTweak;
            public readonly float VoiceHz;
            public readonly float Tract;

            public Spec(string name, string donor, Color colour, float massTweak, float widthTweak,
                        float voiceHz, float tract)
            {
                Name = name;
                Donor = donor;
                Colour = colour;
                MassTweak = massTweak;
                WidthTweak = widthTweak;
                VoiceHz = voiceHz;
                Tract = tract;
            }
        }

        // Colours avoid the family's reserved RED (heuristic bot) and plain GREEN
        // (the reference brain), and sit apart from Matt's orange, Grandma's
        // green, Nick's blue and Kim's violet. Voices follow the face: the two
        // small children are pitched high with a short tract.
        private static readonly Spec[] Specs =
        {
            new Spec("Tong",    "Matt",    new Color(0.98f, 0.80f, 0.20f),  0.03f, -0.02f, 300f, 0.70f),
            new Spec("Alan",    "Matt",    new Color(0.20f, 0.70f, 0.75f), -0.02f,  0.02f, 115f, 0.98f),
            new Spec("Josh",    "Matt",    new Color(0.55f, 0.35f, 0.20f),  0.04f,  0.03f, 105f, 1.02f),
            new Spec("Maya",    "Grandma", new Color(0.95f, 0.55f, 0.75f), -0.03f, -0.03f, 310f, 0.70f),
            new Spec("Baily",   "Grandma", new Color(0.85f, 0.85f, 0.88f),  0.02f,  0.00f, 205f, 0.85f),
            new Spec("Porto",   "Grandma", new Color(0.10f, 0.55f, 0.55f),  0.04f,  0.03f, 100f, 1.03f),
            new Spec("Brendan", "Nick",    new Color(0.45f, 0.75f, 0.95f),  0.03f,  0.02f,  95f, 1.06f),
            new Spec("Decoy",   "Nick",    new Color(0.80f, 0.65f, 0.40f), -0.02f, -0.02f, 120f, 0.97f),
            new Spec("Sokol",   "Nick",    new Color(0.35f, 0.30f, 0.65f),  0.02f,  0.03f, 100f, 1.04f),
            new Spec("Dupee",   "Kim",     new Color(0.95f, 0.70f, 0.55f), -0.03f, -0.03f, 112f, 1.00f),
            new Spec("Velez",   "Kim",     new Color(0.85f, 0.35f, 0.55f),  0.03f,  0.02f, 108f, 1.05f),
            new Spec("Manzi",   "Kim",     new Color(0.65f, 0.65f, 0.20f), -0.02f,  0.02f, 118f, 0.99f),
        };

        /// The voices the retired GeneratePlaceholderVoices tool gave the four.
        private static void TrainedVoice(string name, out float hz, out float tract)
        {
            switch (name)
            {
                case "Kim":     hz = 196f; tract = 1.14f; return;
                case "Nick":    hz = 128f; tract = 1.04f; return;
                case "Grandma": hz = 110f; tract = 1.00f; return;
                default:        hz = 98f;  tract = 0.94f; return;   // Matt
            }
        }

        [MenuItem("PoSumo/Generate Placeholder Fighters")]
        public static void Run()
        {
            var report = new StringBuilder();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Debug.LogError("PLACEHOLDER FIGHTERS RESULT: FAILED — exit Play mode first (it edits a scene).");
                    return;
                }

                int faces = FixFaceImports();
                report.AppendLine($"  face imports checked: {faces}");

                var roster = new List<string>();
                foreach (string name in Trained)
                {
                    Agent_CharacterDefinition trained = LoadCharacter(name);
                    if (trained == null)
                    {
                        throw new InvalidOperationException($"trained fighter '{name}' has no character asset");
                    }
                    if (trained.voicePitchHz <= 0f)
                    {
                        TrainedVoice(name, out trained.voicePitchHz, out trained.voiceTract);
                        EditorUtility.SetDirty(trained);
                    }
                    roster.Add(name);
                }

                foreach (Spec spec in Specs)
                {
                    CreateOrUpdate(spec, report);
                    roster.Add(spec.Name);
                }
                AssetDatabase.SaveAssets();

                WireRoster(roster);
                report.AppendLine($"  {TOURNAMENT_SCENE}: roster = {roster.Count} fighters");

                Debug.Log("PLACEHOLDER FIGHTERS RESULT: OK\n" + report);
            }
            catch (Exception e)
            {
                Debug.LogError($"PLACEHOLDER FIGHTERS RESULT: FAILED — {e}\n{report}");
            }
        }

        private static Agent_CharacterDefinition LoadCharacter(string name) =>
            AssetDatabase.LoadAssetAtPath<Agent_CharacterDefinition>($"{AGENTS_DIR}/{name}_v01/{name}_Character.asset");

        private static void CreateOrUpdate(Spec spec, StringBuilder report)
        {
            Agent_CharacterDefinition donor = LoadCharacter(spec.Donor);
            if (donor == null || donor.inferenceModel == null)
            {
                throw new InvalidOperationException($"donor '{spec.Donor}' has no character asset or no brain");
            }

            string folder = $"{AGENTS_DIR}/{spec.Name}_v01";
            string path = $"{folder}/{spec.Name}_Character.asset";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(AGENTS_DIR, $"{spec.Name}_v01");
            }

            // Every field from the donor — brain generation, reward shaping,
            // clothing — then the identity on top. Copying onto an EXISTING asset
            // keeps its GUID, so the roster reference survives a re-run.
            Agent_CharacterDefinition character = AssetDatabase.LoadAssetAtPath<Agent_CharacterDefinition>(path);
            bool created = character == null;
            if (created)
            {
                character = ScriptableObject.CreateInstance<Agent_CharacterDefinition>();
            }
            EditorUtility.CopySerialized(donor, character);
            character.name = $"{spec.Name}_Character";
            character.behaviorName = spec.Name;
            character.teamColor = spec.Colour;
            character.faceArtName = string.Empty;          // convention: <behaviorName>_*
            character.faceArtFacesLeft = false;            // the fake profiles face right
            character.headSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{FACES_DIR}/{spec.Name}_Neutral.png");
            character.massScale = donor.massScale * (1f + spec.MassTweak);
            character.widthScale = donor.widthScale * (1f + spec.WidthTweak);
            character.torqueScale = donor.torqueScale;
            character.voicePitchHz = spec.VoiceHz;
            character.voiceTract = spec.Tract;
            character.borrowedBrainFrom = spec.Donor;
            // The CLOTHING colours are the donor's; garments are tinted to the team
            // colour at runtime (Agent_BipedBody), so they follow the new identity.

            if (created)
            {
                AssetDatabase.CreateAsset(character, path);
            }
            EditorUtility.SetDirty(character);

            WriteManifest(folder, spec, donor);
            report.AppendLine($"  {(created ? "created" : "updated")} {spec.Name} (brain {spec.Donor}, " +
                              $"mass x{character.massScale:F2}, width x{character.widthScale:F2}" +
                              (character.headSprite == null ? ", NO FACE — run Tools/make_placeholder_faces.py" : "") + ")");
        }

        private static void WriteManifest(string folder, Spec spec, Agent_CharacterDefinition donor)
        {
            string text =
$@"# {spec.Name} — PLACEHOLDER fighter

Generated by *PoSumo → Generate Placeholder Fighters* (2026-11-10) to fill the 16-slot
tournament bracket until {spec.Name} is trained.

| | |
|---|---|
| Brain | **borrowed** from **{spec.Donor}** (`{AssetDatabase.GetAssetPath(donor.inferenceModel)}`) |
| Body | {spec.Donor}'s, mass ×{1f + spec.MassTweak:F2}, torso width ×{1f + spec.WidthTweak:F2} |
| Face | `Assets/Art/Faces/{spec.Name}_Neutral.png` — a FAKE right-facing profile cut from a front photo by `Tools/make_placeholder_faces.py`; every mood/level is drawn procedurally from it until real `{spec.Name}_<Mood>_<1-5>.png` art exists |
| Voice | fully synthesised (`Systems_VoiceSynth`, {spec.VoiceHz:F0} Hz, tract {spec.Tract:F2}) until real `{spec.Name}_<Mood>_L<level>_<variant>.wav` lines exist |

The brain was trained on {spec.Donor}'s body, so this fighter fights like a slightly
heavier or lighter {spec.Donor}. To make it a real fighter: create `Training/configs/{spec.Name}<Phase>01.yaml`
with `behaviors: {spec.Name}`, train it, deploy `{spec.Name}.onnx` into this folder, and clear
`borrowedBrainFrom` on the character asset.
";
            File.WriteAllText(Path.Combine(folder, "MANIFEST.md"), text);
        }

        /// Sprite, single, uncompressed, no mips — what the FaceAtlas and the
        /// unlit head material expect. A no-op on files already set that way.
        private static int FixFaceImports()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { FACES_DIR });
            int changed = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                {
                    continue;
                }
                bool dirty = false;
                if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; dirty = true; }
                if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; dirty = true; }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
                if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
                if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; dirty = true; }
                if (dirty)
                {
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            return changed;
        }

        /// Wired by NAME, each fighter re-loaded from its conventional path. The
        /// first version passed the objects CreateOrUpdate had just written, and
        /// after SaveAssets those C# objects no longer back the saved assets:
        /// measured, twelve {fileID: 0} entries in the saved scene.
        private static void WireRoster(IReadOnlyList<string> names)
        {
            var scene = EditorSceneManager.OpenScene(TOURNAMENT_SCENE, OpenSceneMode.Single);
            Systems_TournamentBracket bracket = UnityEngine.Object.FindAnyObjectByType<Systems_TournamentBracket>();
            if (bracket == null)
            {
                throw new InvalidOperationException("SCN_TOURNAMENT has no Systems_TournamentBracket");
            }
            var serialized = new SerializedObject(bracket);
            SerializedProperty array = serialized.FindProperty(ROSTER_FIELD);
            array.arraySize = names.Count;
            for (int index = 0; index < names.Count; index++)
            {
                Agent_CharacterDefinition persistent = LoadCharacter(names[index]);
                if (persistent == null)
                {
                    throw new InvalidOperationException($"no character asset at {AGENTS_DIR}/{names[index]}_v01/");
                }
                array.GetArrayElementAtIndex(index).objectReferenceValue = persistent;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
#endif
