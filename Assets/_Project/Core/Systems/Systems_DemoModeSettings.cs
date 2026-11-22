using UnityEngine;
using UnityEngine.UIElements;

namespace PoSumo
{
    /// Tuning for the diagnostics overlay (Systems_DiagOverlay) and the idle demo
    /// mode (Systems_DemoMode). One asset, Assets/_Project/Data/Resources/DemoModeSettings.asset,
    /// loaded by name at startup so both systems work in every scene without being
    /// wired into one.
    ///
    /// Everything a designer would want to change lives here, not in code: how long
    /// the game waits without input before it shows itself off, how long each
    /// showcase camera holds, which cameras the showcase cycles through, and the
    /// panel the overlay draws on.
    [CreateAssetMenu(menuName = "PoSumo/Demo Mode Settings", fileName = "DemoModeSettings")]
    public sealed class Systems_DemoModeSettings : ScriptableObject
    {
        /// Resources path the runtime loads.
        public const string RESOURCE_PATH = "DemoModeSettings";

        [Header("Demo mode")]
        [Tooltip("Seconds without ANY input (keyboard, mouse, touch, gamepad) — counted from load or from the last input — before demo mode starts.")]
        [Min(1f)] public float idleSeconds = 30f;

        [Tooltip("Frames longer than this (seconds) do not count toward the idle time: the WebGL shader warm-up, a background tab or a scene load is not the visitor being idle.")]
        [Min(0.02f)] public float maxCountedFrameSeconds = 0.25f;

        [Tooltip("Seconds each showcase camera holds before the next one.")]
        [Min(1f)] public float cameraDwellSeconds = 7f;

        [Tooltip("The showcase camera shots, cycled in order while demo mode runs in the arena.")]
        public Systems_CameraFollow.CameraMode[] cameraShots =
        {
            Systems_CameraFollow.CameraMode.Broadcast,
            Systems_CameraFollow.CameraMode.Follow,
            Systems_CameraFollow.CameraMode.Orbit,
        };

        [Tooltip("On the bracket (menu) screen, demo mode starts the tournament on auto-play, so the showcase is real bouts between the trained fighters rather than a static menu.")]
        public bool startTournamentFromMenu = true;

        [Header("Diagnostics overlay (TAB / DIAG button)")]
        [Tooltip("PanelSettings the overlay draws on. The game's own panel, so it scales exactly like the HUD.")]
        public PanelSettings overlayPanelSettings;

        [Tooltip("UIDocument sorting order of the overlay: above every HUD document on the same panel.")]
        public int overlaySortingOrder = 900;

        [Tooltip("Seconds between overlay refreshes.")]
        [Min(0.05f)] public float overlayRefreshSeconds = 0.25f;

        [Tooltip("How many decision steps the inference-latency rolling average covers.")]
        [Min(1)] public int latencyWindow = 30;

        [Header("Training-speed benchmark (BENCHMARK button in the overlay)")]
        [Tooltip("Real seconds the benchmark runs the live arena simulation flat out.")]
        [Min(0.5f)] public float benchmarkSeconds = 3f;

        [Tooltip("Time scale the benchmark asks for (training envs run at 20). Physics stays on its fixed 0.02 s step; only how many steps fit in a real second changes.")]
        [Min(1f)] public float benchmarkTimeScale = 20f;
    }
}
