using UnityEngine;

namespace PoSumo
{
    /// Command-line switches a TRAINING env player can be launched with
    /// (mlagents-learn `--env-args`, or `env_settings.env_args` in the YAML).
    ///
    /// Exists for one reason: a vector-shape flag such as `balanceObservations`
    /// must be OFF on the character sheet while the shipped brain is the one
    /// without it, and ON in the env that trains its replacement. The env build
    /// snapshots the sheet, so flipping the sheet to build the env and back again
    /// is exactly the kind of manual step that ships a STALE BRAIN. With this the
    /// sheet stays as the game needs it and the config asks for the wider vector.
    ///
    /// Inert in the Editor and in the game, which never pass the arguments — same
    /// rule as `Systems_TrainingFighter`.
    public static class Systems_TrainingFlags
    {
        private const string BALANCE_ARG = "-posumoBalanceObs";

        private static bool _resolved;
        private static bool _balance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlaySessionStart()
        {
            _resolved = false;
            _balance = false;
        }

        /// True when this env player was launched with -posumoBalanceObs.
        public static bool ForceBalanceObservations
        {
            get
            {
                Resolve();
                return _balance;
            }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (Application.isEditor) return;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int argIndex = 0; argIndex < args.Length; argIndex++)
            {
                if (string.Equals(args[argIndex], BALANCE_ARG, System.StringComparison.OrdinalIgnoreCase))
                {
                    _balance = true;
                    Systems_Log.Info("[TRAINFLAGS] balance observations forced ON for this env");
                }
            }
        }
    }
}
