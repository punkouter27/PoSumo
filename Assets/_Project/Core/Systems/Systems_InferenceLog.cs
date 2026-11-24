using System.Collections.Generic;
using UnityEngine;

namespace PoSumo
{
    /// One console line per brain, the first time its ONNX policy acts in this session:
    /// "[PoSumo] <brain>.onnx inference running (...)". It is the proof, in a player's
    /// browser console (the local WebGL build at 16:9 and 9:16, AGENTS.md section 3),
    /// that the trained policy really drives the fighter. Agent_Biped.OnActionReceived
    /// calls Note once per fighter; the set makes a rematch of the same brain silent.
    ///
    /// Plain Debug.Log on purpose: Systems_Log.Info is compiled out of release players,
    /// and this line must reach the shipped WebGL build's console.
    public static class Systems_InferenceLog
    {
        public const string MARKER = "inference running";
        private static readonly HashSet<string> Seen = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Seen.Clear();

        /// The line for one brain (pure, for the EditMode test).
        public static string Line(string brain, string detail) => $"[PoSumo] {brain}.onnx {MARKER} ({detail})";

        /// Logs the line for this brain once; true when it logged now.
        public static bool Note(string brain, string detail)
        {
            if (string.IsNullOrEmpty(brain) || !Seen.Add(brain)) return false;
            Debug.Log(Line(brain, detail));
            return true;
        }

        /// True once this brain's line has been logged this session.
        public static bool Logged(string brain) => brain != null && Seen.Contains(brain);

        /// Tests: forget which brains were logged.
        public static void Clear() => Seen.Clear();
    }
}
