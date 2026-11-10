using UnityEngine;

namespace PoSumo
{
    /// The build date as YYMMDD — the stamp in the bottom-right corner of every
    /// screen (UU_AGENT rule, 2026-11-10: "Show the build date (YYMMDD) in the
    /// bottom-right corner").
    ///
    /// The date is written at BUILD time, not read at run time: a player cannot
    /// know when a binary was built, and `DateTime.Now` on the phone would print
    /// today's date for an APK that is a month old — which is the one question
    /// this stamp exists to answer when two sideloaded installs look identical.
    /// `Assets/Editor/WriteBuildStamp.cs` writes `Resources/BuildStamp.txt` in an
    /// `IPreprocessBuildWithReport`, so every player carries the day it was made.
    ///
    /// In the Editor there is no build, so the stamp is today's date — which is
    /// what an Editor session effectively is. Outside the Editor a missing file is
    /// reported as dashes rather than guessed.
    public static class Systems_BuildStamp
    {
        /// Resource path of the stamp file the build preprocessor writes.
        public const string RESOURCE_PATH = "BuildStamp";

        /// The format both writer and reader agree on.
        public const string FORMAT = "yyMMdd";

        private static string _cached;

        /// Domain reload is off in this project, so a cached value would survive
        /// into the next Play session — harmless for a build constant, but in the
        /// Editor the fallback is TODAY, and a session left open overnight would
        /// otherwise keep yesterday.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _cached = null;
        }

        /// The stamp, e.g. "261110". Resolved once per session.
        public static string Date
        {
            get
            {
                if (_cached != null)
                {
                    return _cached;
                }
#if UNITY_EDITOR
                _cached = System.DateTime.Now.ToString(FORMAT,
                    System.Globalization.CultureInfo.InvariantCulture);
#else
                TextAsset stamp = Resources.Load<TextAsset>(RESOURCE_PATH);
                _cached = stamp != null && !string.IsNullOrWhiteSpace(stamp.text)
                    ? stamp.text.Trim()
                    : "------";
#endif
                return _cached;
            }
        }
    }
}
