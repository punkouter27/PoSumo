using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PoSumo.EditorTools
{
    /// *PoSumo → Copy Brains To Placeholders (Random)*: gives every placeholder
    /// fighter its OWN copy of a randomly chosen trained fighter's `.onnx`, at
    /// `Assets/Agents/<Name>_v01/<Name>.onnx`, and points its character sheet at it.
    ///
    /// Why a copy and not the donor's asset: each placeholder is going to be
    /// retrained into a style of its own (user, 2026-11-10). With its own file, that
    /// retrain is an in-place overwrite like every other deploy — the `.meta` GUID
    /// and the sheet's reference survive — and no placeholder's brain moves when a
    /// donor is retrained.
    ///
    /// `borrowedBrainFrom` is set to the donor drawn, so the fighter still reads as
    /// PLACEHOLDER until it has trained. The brain-generation fields are copied from
    /// that donor, or the model would be fed a vector it cannot accept. Body, face,
    /// voice and reward shaping are left alone.
    ///
    /// Re-runnable: an existing copy is overwritten in place (GUID kept), and a new
    /// draw is made each time. Logs `PLACEHOLDER BRAINS RESULT:`.
    public static class CopyBrainsToPlaceholders
    {
        private const string AGENTS_DIR = "Assets/Agents";

        [MenuItem("PoSumo/Copy Brains To Placeholders (Random)")]
        public static void Run()
        {
            Run(System.Environment.TickCount);
        }

        public static void Run(int seed)
        {
            var donors = new System.Collections.Generic.List<Agent_CharacterDefinition>();
            var placeholders = new System.Collections.Generic.List<Agent_CharacterDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:Agent_CharacterDefinition", new[] { AGENTS_DIR }))
            {
                var character = AssetDatabase.LoadAssetAtPath<Agent_CharacterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (character == null) continue;
                if (character.IsPlaceholder) placeholders.Add(character);
                else if (character.inferenceModel != null) donors.Add(character);
            }
            if (donors.Count == 0)
            {
                Debug.LogError("PLACEHOLDER BRAINS RESULT: FAIL — no trained fighter with a model to copy from");
                return;
            }

            var random = new System.Random(seed);
            var report = new StringBuilder($"PLACEHOLDER BRAINS RESULT: {placeholders.Count} placeholder(s), seed {seed}\n");
            foreach (Agent_CharacterDefinition placeholder in placeholders)
            {
                Agent_CharacterDefinition donor = donors[random.Next(donors.Count)];
                string donorPath = AssetDatabase.GetAssetPath(donor.inferenceModel);
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(placeholder)).Replace('\\', '/');
                string targetPath = $"{folder}/{placeholder.behaviorName}.onnx";

                if (File.Exists(targetPath))
                {
                    // Overwrite IN PLACE so the existing .meta GUID survives.
                    File.Copy(donorPath, targetPath, true);
                    AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
                }
                else if (!AssetDatabase.CopyAsset(donorPath, targetPath))
                {
                    report.AppendLine($"  {placeholder.behaviorName}: COPY FAILED from {donorPath}");
                    continue;
                }

                var model = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(targetPath);
                if (model == null)
                {
                    report.AppendLine($"  {placeholder.behaviorName}: copied but no ModelAsset at {targetPath}");
                    continue;
                }

                placeholder.inferenceModel = model;
                placeholder.borrowedBrainFrom = donor.behaviorName;
                placeholder.extendedObservations = donor.extendedObservations;
                placeholder.contactObservations = donor.contactObservations;
                placeholder.staminaObservation = donor.staminaObservation;
                placeholder.intentObservations = donor.intentObservations;
                placeholder.decisionPeriod = donor.decisionPeriod;
                EditorUtility.SetDirty(placeholder);
                report.AppendLine($"  {placeholder.behaviorName} <- {donor.behaviorName} ({targetPath})");
            }
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }
    }
}
