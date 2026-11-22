using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PoSumo.EditorTools
{
    /// Writes the YYMMDD build date into `Assets/_Project/Data/Resources/BuildStamp.txt`
    /// immediately before every player build, so `Systems_BuildStamp.Date` reports
    /// the day the binary was MADE rather than the day it is being run.
    ///
    /// Runs for every target (Android, WebGL, the headless training envs) — the
    /// stamp is cheap and a desktop env player showing its build date is useful
    /// too. The file is gitignored: it changes on every build and carries no
    /// information git does not already have.
    public sealed class WriteBuildStamp : IPreprocessBuildWithReport
    {
        private const string ASSET_PATH = "Assets/_Project/Data/Resources/BuildStamp.txt";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            // ONE stamp: scripts/publish_webgl.ps1 computes the UTC yyMMdd once and
            // hands it over as PO_BUILD_VERSION, so the player, index.html and
            // diag.html all carry the same value. A menu build without the script
            // falls back to today's UTC date.
            string stamp = System.Environment.GetEnvironmentVariable("PO_BUILD_VERSION");
            if (string.IsNullOrWhiteSpace(stamp))
            {
                stamp = System.DateTime.UtcNow.ToString(PoSumo.Systems_BuildStamp.FORMAT,
                                                        CultureInfo.InvariantCulture);
            }
            stamp = stamp.Trim();
            // The WebGL page template prints v{PRODUCT_VERSION} in its footer: same YYMMDD there.
            if (report.summary.platform == BuildTarget.WebGL)
            {
                PlayerSettings.bundleVersion = stamp;
            }
            File.WriteAllText(ASSET_PATH, stamp);
            AssetDatabase.ImportAsset(ASSET_PATH, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"BUILD STAMP RESULT: {stamp} -> {ASSET_PATH}");
        }
    }
}
