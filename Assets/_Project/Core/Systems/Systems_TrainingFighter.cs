using UnityEngine;

namespace PoSumo
{
    /// Lets ONE training env train ANY fighter: launch the env player with
    /// `-posumoFighter <BehaviorName>` (mlagents-learn `--env-args -posumoFighter Tong`)
    /// and every agent in the scene becomes that fighter — behavior name, brain
    /// generation, physique and reward coefficients all come from its sheet.
    ///
    /// Why: the twelve placeholder fighters have no SCN_TRAIN_<NAME> scene, and
    /// a fighter must be trained on its OWN physique and under its OWN behavior
    /// name (AGENTS.md §3: no fighter reuses another's brain). They train in
    /// their donor's scene, which already holds the right lane layout, with the
    /// character swapped at load.
    ///
    /// Inert without the argument, and in the Editor: the game never passes it,
    /// so nothing in a bout or a bracket changes. Called from the top of
    /// Agent_Biped.Awake and Agent_BipedBody.Awake, which both read `character`.
    public static class Systems_TrainingFighter
    {
        private const string ARG = "-posumoFighter";

        private static bool _resolved;
        private static Agent_CharacterDefinition _override;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlaySessionStart()
        {
            _resolved = false;
            _override = null;
        }

        /// The fighter named on the command line, or `current` when none is.
        public static Agent_CharacterDefinition Resolve(Agent_CharacterDefinition current)
        {
            if (!_resolved)
            {
                _resolved = true;
                _override = Lookup();
            }
            return _override != null ? _override : current;
        }

        private static Agent_CharacterDefinition Lookup()
        {
            if (Application.isEditor)
            {
                return null;
            }
            string[] args = System.Environment.GetCommandLineArgs();
            for (int argIndex = 0; argIndex < args.Length - 1; argIndex++)
            {
                if (!string.Equals(args[argIndex], ARG, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string wanted = args[argIndex + 1];
                var registry = Resources.Load<Systems_FighterRegistry>(Systems_FighterRegistry.RESOURCE_PATH);
                Agent_CharacterDefinition found = registry != null ? registry.Find(wanted) : null;
                if (found == null)
                {
                    // Loud: a run that silently trains the donor under the donor's
                    // name would overwrite nothing and teach the placeholder nothing.
                    Debug.LogError($"[TRAINFIGHTER] {ARG} {wanted}: no such fighter in Resources/{Systems_FighterRegistry.RESOURCE_PATH}. Training the scene's own fighter.");
                    return null;
                }
                Systems_Log.Info($"[TRAINFIGHTER] this env trains {found.behaviorName}");
                return found;
            }
            return null;
        }
    }
}
