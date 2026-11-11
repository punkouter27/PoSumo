using System.IO;
using UnityEditor;
using UnityEngine;

namespace PoSumo.EditorTools
{
    /// Copies a finished training run's ONNX into the character's agent folder
    /// and points that character's `inferenceModel` at it.
    ///
    /// The ONNX is always overwritten IN PLACE so the .meta (and therefore the
    /// asset GUID) survives — any scene or asset already referencing the model
    /// keeps working. Creating a fresh file instead would mint a new GUID and
    /// silently break those references.
    public static class DeployBrain
    {
        private const string RESULTS_DIR = "Training/results";

        // ---- Graduation gate (AGENTS.md rule 4, 2026-11-10) -------------------
        //
        // A run's brain is exported to the game only when Tools/graduation_gate.py
        // has written a PASSING GATE.json into that run's directory: 100 straight
        // successful episodes and no NaN anywhere. Without one the deploy is
        // REFUSED with a DEPLOY RESULT line saying why.
        //
        // The toggle below exists because short corrective runs (the 5-minute
        // Unique01 waves, a 60-minute warm start) can never reach a 100-episode
        // streak, and the user decides whether such a brain ships. It is an
        // EditorPrefs value, so it is per machine and off by default.
        private const string UNGATED_PREF = "PoSumo.AllowUngatedDeploy";
        private const string UNGATED_MENU = "PoSumo/Allow Ungated Brain Deploy";

        [MenuItem(UNGATED_MENU)]
        private static void ToggleUngated()
        {
            EditorPrefs.SetBool(UNGATED_PREF, !EditorPrefs.GetBool(UNGATED_PREF, false));
        }

        [MenuItem(UNGATED_MENU, true)]
        private static bool ToggleUngatedValidate()
        {
            Menu.SetChecked(UNGATED_MENU, EditorPrefs.GetBool(UNGATED_PREF, false));
            return true;
        }

        /// The run directory a deploy source lives in: the folder directly under
        /// Training/results, whatever depth the .onnx sits at.
        private static string RunDirOf(string source)
        {
            string full = Path.GetFullPath(source).Replace('\\', '/');
            string results = Path.GetFullPath(RESULTS_DIR).Replace('\\', '/').TrimEnd('/') + "/";
            if (!full.StartsWith(results, System.StringComparison.OrdinalIgnoreCase)) return null;
            string rest = full.Substring(results.Length);
            int slash = rest.IndexOf('/');
            return slash < 0 ? null : results + rest.Substring(0, slash);
        }

        /// Null when the run may ship; otherwise the reason it may not.
        private static string GateRefusal(string source)
        {
            string runDir = RunDirOf(source);
            if (runDir == null) return null;   // not a training run (a hand-staged file)
            string gate = Path.Combine(runDir, "GATE.json");
            if (!File.Exists(gate))
            {
                return $"no GATE.json in {runDir} - run Training/venv/Scripts/python.exe " +
                       $"Tools/graduation_gate.py {runDir} --behavior <Name>";
            }
            string json = File.ReadAllText(gate);
            if (System.Text.RegularExpressions.Regex.IsMatch(json, @"""passed""\s*:\s*true")) return null;
            return $"GATE.json in {runDir} says the run did NOT pass: " +
                   System.Text.RegularExpressions.Regex.Match(json, @"""reasons""[^\]]*\]").Value;
        }

        // The menu items no longer pin a run id. They used to hold string literals
        // (*_unified01 -> *_stamina01 -> ...) that rotted every time the campaign
        // moved on: nothing checked them until a human clicked the menu, and the
        // runs they named were pruned long before anyone did. Training/results is
        // gitignored and absent from a fresh clone, so a literal can never be
        // right there either.
        //
        // Each item now resolves the NEWEST finished run for its fighter — the
        // most recently written `<name>_*/<Behavior>.onnx` under Training/results,
        // case-insensitive on the run prefix — by write time, for the same reason
        // DeployLatestCheckpoint picks by write time: a `--force` that TensorBoard
        // blocked leaves an older run's files beside the new one's, and a name
        // sort would happily ship the stale run.
        //
        // These are the UNIFIED runs: since the walk and fight brains were merged
        // there is one run and one ONNX per fighter, not two.
        [MenuItem("PoSumo/Deploy Matt Brain")]
        public static void DeployMatt()
        {
            DeployNewest("Matt", "Assets/Agents/Matt_v01");
        }

        [MenuItem("PoSumo/Deploy Grandma Brain")]
        public static void DeployGrandma()
        {
            // Folder and ONNX stem both follow the behavior name (Grandma). Any
            // run launched from GrandmaRebuild01.yaml exports `Grandma.onnx` under
            // a `grandma_*` run id, which is what this resolves by prefix.
            DeployNewest("Grandma", "Assets/Agents/Grandma_v01");
        }

        [MenuItem("PoSumo/Deploy Nick Brain")]
        public static void DeployNick()
        {
            DeployNewest("Nick", "Assets/Agents/Nick_v01");
        }

        [MenuItem("PoSumo/Deploy Kim Brain")]
        public static void DeployKim()
        {
            DeployNewest("Kim", "Assets/Agents/Kim_v01");
        }

        /// Deploys the newest finished run whose id starts with `<behavior>_`
        /// (case-insensitive) and holds a final `<Behavior>.onnx` export.
        public static void DeployNewest(string behaviorName, string agentFolder)
        {
            string runId = ResolveNewestRun(behaviorName);
            if (runId == null)
            {
                return;   // ResolveNewestRun already logged the DEPLOY RESULT line
            }
            Deploy(runId, behaviorName, agentFolder);
        }

        /// Newest run directory under Training/results for `behaviorName`, judged
        /// by the write time of its final ONNX export. Null (after logging a
        /// DEPLOY RESULT failure) when the results directory is absent or holds no
        /// finished run for that fighter.
        private static string ResolveNewestRun(string behaviorName)
        {
            if (!Directory.Exists(RESULTS_DIR))
            {
                Debug.LogError($"DEPLOY RESULT: Failed — {RESULTS_DIR} absent. It is gitignored: re-run " +
                               "training locally to recreate it, or copy a run in from elsewhere.");
                return null;
            }

            string prefix = behaviorName + "_";
            string newestRun = null;
            System.DateTime newestWrite = System.DateTime.MinValue;

            foreach (string runDir in Directory.GetDirectories(RESULTS_DIR))
            {
                string runId = Path.GetFileName(runDir);
                if (!runId.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string onnx = Path.Combine(runDir, behaviorName + ".onnx");
                if (!File.Exists(onnx))
                {
                    continue;
                }
                System.DateTime write = File.GetLastWriteTimeUtc(onnx);
                if (newestRun == null || write > newestWrite)
                {
                    newestWrite = write;
                    newestRun = runId;
                }
            }

            if (newestRun == null)
            {
                Debug.LogError($"DEPLOY RESULT: Failed — no run under {RESULTS_DIR} named {prefix}* holds a " +
                               $"finished {behaviorName}.onnx. The run must have finished (or been stopped) " +
                               "so the trainer exported it.");
            }
            return newestRun;
        }

        /// Ships all four in one click. Added because deploying the roster was four
        /// menu trips, and a half-deployed roster (two fighters on a new generation,
        /// two on the old) is silently wrong rather than broken — both ONNX files
        /// load fine, they just came from different training runs.
        [MenuItem("PoSumo/Deploy ALL Brains")]
        public static void DeployAll()
        {
            DeployGrandma();
            DeployMatt();
            DeployNick();
            DeployKim();
        }

        /// Deploy the newest numbered checkpoint of a RUNNING training run, so a
        /// brain can be tried before the run finishes. The trainer only writes
        /// the unnumbered `<Behavior>.onnx` on shutdown, hence the separate path.
        /// Safe while training continues — this only reads the exported files.
        ///
        /// No menu item: the run id changes every run, so this is invoked by
        /// name through MCP `script-execute` while a run is in flight. Finished
        /// runs keep only their final export, so this finds nothing in them.
        ///
        /// Picks by WRITE TIME, not by step number, and the difference is not
        /// academic. `--force` silently fails to clear a run directory when
        /// TensorBoard is holding it open (see Training/README.md), which leaves
        /// the discarded run's checkpoints sitting next to the new one's. Those
        /// leftovers carry HIGHER step numbers than a restarted run reaches for
        /// hours, so a highest-step rule ships a brain from the run you deleted —
        /// trained on whatever physics you just changed. Write time is monotonic
        /// within a run and always identifies the live one. Ties (the trainer
        /// exports two checkpoints in the same instant on shutdown) break by step.
        public static void DeployLatestCheckpoint(string runId, string behaviorName, string agentFolder)
        {
            string dir = $"{RESULTS_DIR}/{runId}/{behaviorName}";
            if (!Directory.Exists(dir))
            {
                Debug.LogError($"DEPLOY RESULT: Failed — no checkpoint directory at {dir}.");
                return;
            }

            string newest = null;
            long newestStep = -1;
            System.DateTime newestWrite = System.DateTime.MinValue;
            long highestStepSeen = -1;

            foreach (string file in Directory.GetFiles(dir, $"{behaviorName}-*.onnx"))
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                int dash = stem.LastIndexOf('-');
                if (dash < 0) continue;
                if (!long.TryParse(stem.Substring(dash + 1), out long step)) continue;

                if (step > highestStepSeen) highestStepSeen = step;

                System.DateTime write = File.GetLastWriteTimeUtc(file);
                if (write > newestWrite || (write == newestWrite && step > newestStep))
                {
                    newestWrite = write;
                    newestStep = step;
                    newest = file;
                }
            }

            if (newest == null)
            {
                Debug.LogError($"DEPLOY RESULT: Failed — no {behaviorName}-<step>.onnx checkpoints in {dir}.");
                return;
            }

            // Loud, because silence here is how a stale brain ships unnoticed.
            if (highestStepSeen > newestStep)
            {
                Debug.LogWarning(
                    $"DEPLOY: {dir} still holds checkpoints from a DISCARDED run — highest step on disk is " +
                    $"{highestStepSeen:N0} but the live run has only reached {newestStep:N0}. Deploying the live " +
                    "one by write time. Delete the leftovers, and kill TensorBoard before the next --force.");
            }

            CopyInto(newest, behaviorName, agentFolder, $"checkpoint @ {newestStep:N0} steps");
        }

        /// runId: folder under Training/results. behaviorName: the ONNX stem the
        /// trainer writes. agentFolder: destination under Assets/Agents.
        public static void Deploy(string runId, string behaviorName, string agentFolder)
        {
            string source = $"{RESULTS_DIR}/{runId}/{behaviorName}.onnx";
            if (!File.Exists(source))
            {
                Debug.LogError($"DEPLOY RESULT: Failed — no ONNX at {source}. " +
                               "The run must have finished (or been stopped) so the trainer exported it.");
                return;
            }

            CopyInto(source, behaviorName, agentFolder, "final export");
        }

        // The separate "Deploy <X> Walk Brain" entries are gone. Walk and fight
        // are one policy per fighter now, told apart by the task flag in the
        // observation vector, so there is a single brain to deploy per fighter and
        // a single slot on the character sheet to point at it.

        private static void CopyInto(string source, string behaviorName, string agentFolder, string note)
        {
            string refusal = GateRefusal(source);
            if (refusal != null)
            {
                if (!EditorPrefs.GetBool(UNGATED_PREF, false))
                {
                    Debug.LogError($"DEPLOY RESULT: Refused — graduation gate: {refusal}. " +
                                   $"To ship it anyway tick {UNGATED_MENU}.");
                    return;
                }
                Debug.LogWarning($"DEPLOY: UNGATED — {refusal}. Shipping because {UNGATED_MENU} is on.");
                note += " | UNGATED";
            }
            Directory.CreateDirectory(agentFolder);
            string destination = $"{agentFolder}/{behaviorName}.onnx";
            File.Copy(source, destination, overwrite: true);
            // ForceSynchronousImport, not just ForceUpdate. The load below runs on
            // the very next line, and a plain ForceUpdate import can still be
            // queued at that point — so the ModelAsset comes back null and this
            // reports a deploy failure for a file that is perfectly fine. It shows
            // up whenever the editor is starved, which is exactly when you are
            // deploying: mid-session with training envs saturating the machine.
            AssetDatabase.ImportAsset(destination,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            var model = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(destination);
            if (model == null)
            {
                Debug.LogError($"DEPLOY RESULT: Failed — {destination} did not import as a ModelAsset.");
                return;
            }

            string characterPath = $"{agentFolder}/{behaviorName}_Character.asset";
            var character = AssetDatabase.LoadAssetAtPath<Agent_CharacterDefinition>(characterPath);
            if (character == null)
            {
                Debug.LogError($"DEPLOY RESULT: Failed — no character asset at {characterPath}.");
                return;
            }

            character.inferenceModel = model;
            EditorUtility.SetDirty(character);
            string flagNote = SyncBalanceFlag(model, character);
            AssetDatabase.SaveAssets();
            note += flagNote;

            var info = new FileInfo(destination);
            Debug.Log($"DEPLOY RESULT: Succeeded | {behaviorName} | {note} | {info.Length / 1024}KB | " +
                      $"{source} -> {destination} | " +
                      "character.inferenceModel set");
        }

        /// The only vector flag a Real01 run changes is balanceObservations (trained
        /// with -posumoBalanceObs while the sheet stayed off). Reads the deployed
        /// model's input width and sets the flag on every sheet whose
        /// inferenceModel IS this file, so none of them ships a STALE BRAIN.
        private static string SyncBalanceFlag(Unity.InferenceEngine.ModelAsset model,
                                              Agent_CharacterDefinition character)
        {
            int width = -1;
            try
            {
                var loaded = Unity.InferenceEngine.ModelLoader.Load(model);
                var shape = loaded.inputs[0].shape;
                if (!shape.isRankDynamic && shape.rank > 0) width = shape.Get(shape.rank - 1);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"DEPLOY: could not read {model.name}'s input width ({e.GetType().Name}); " +
                                 "observation flags left as they are.");
                return "";
            }
            int without = Agent_Biped.ObservationCount + (character.contactObservations ? 4 : 0)
                          + (character.staminaObservation ? 1 : 0) + (character.extendedObservations ? 3 : 0)
                          + (character.intentObservations ? 2 : 0);
            bool want;
            if (width == without) want = false;
            else if (width == without + 2) want = true;
            else
            {
                Debug.LogError($"DEPLOY: {model.name} takes {width} inputs; the sheet builds {without} " +
                               $"(+2 with balance). No flag change fixes that — this brain will be STALE.");
                return " | STALE WIDTH";
            }

            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Agent_CharacterDefinition"))
            {
                var sheet = AssetDatabase.LoadAssetAtPath<Agent_CharacterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (sheet == null) continue;
                // Only sheets that RUN this very file. Placeholders that borrow this
                // fighter's brain hold their OWN copy of the donor's .onnx (e.g.
                // Dupee.onnx), still at the old width: flipping their flag made all
                // four go STALE and limp in the first bracket after a Real01 deploy.
                // They pick the new width up when their own copy is replaced.
                bool runsThisModel = sheet == character || sheet.inferenceModel == model;
                if (!runsThisModel || sheet.balanceObservations == want) continue;
                sheet.balanceObservations = want;
                EditorUtility.SetDirty(sheet);
                changed++;
            }
            return changed > 0 ? $" | balanceObservations={want} on {changed} sheet(s)" : "";
        }
    }
}
