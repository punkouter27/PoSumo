using NUnit.Framework;
using PoSumo;
using UnityEngine;

namespace PoSumo.Tests
{
    /// The Po Game Standard pieces PoSumo added on 2026-11-20: the inference-latency
    /// rolling average the TAB overlay shows, the editor-tunable demo-mode asset, and
    /// the three penalty terms carried by Reward_Context.
    public sealed class DiagnosticsTests
    {
        [TearDown]
        public void ResetClock()
        {
            Systems_InferenceClock.Window = 30;
            Systems_InferenceClock.ResetWindow();
        }

        [Test]
        public void InferenceClockAveragesOnlyTheLastWindowOfSamples()
        {
            Systems_InferenceClock.Window = 3;
            Systems_InferenceClock.ResetWindow();
            Systems_InferenceClock.Add(10f);
            Systems_InferenceClock.Add(20f);
            Assert.AreEqual(15f, Systems_InferenceClock.AverageMs, 1e-4f);
            Systems_InferenceClock.Add(30f);
            Systems_InferenceClock.Add(40f);   // pushes 10 out of the window
            Assert.AreEqual(30f, Systems_InferenceClock.AverageMs, 1e-4f);
            Assert.AreEqual(40f, Systems_InferenceClock.LastMs, 1e-4f);
            Assert.AreEqual(4, Systems_InferenceClock.Samples);
        }

        [Test]
        public void InferenceClockIgnoresNonFiniteAndNegativeSamples()
        {
            Systems_InferenceClock.ResetWindow();
            Systems_InferenceClock.Add(float.NaN);
            Systems_InferenceClock.Add(-1f);
            Assert.AreEqual(0, Systems_InferenceClock.Samples);
            Assert.AreEqual(0f, Systems_InferenceClock.AverageMs);
        }

        [Test]
        public void DemoModeSettingsAssetLoadsWithTheStandardThirtySecondThreshold()
        {
            var settings = Resources.Load<Systems_DemoModeSettings>(Systems_DemoModeSettings.RESOURCE_PATH);
            Assert.IsNotNull(settings, "Assets/Resources/DemoModeSettings.asset is missing");
            Assert.AreEqual(30f, settings.idleSeconds, 1e-4f);
            Assert.That(settings.cameraDwellSeconds, Is.InRange(6f, 8f));
            Assert.That(settings.cameraShots, Is.Not.Empty);
            Assert.IsNotNull(settings.overlayPanelSettings, "the overlay needs the game's PanelSettings");
        }

        [Test]
        public void RewardContextCarriesTheJointAndActionRatePenaltyInputs()
        {
            var context = new Reward_Context(1f, 0f, Vector2.zero, Vector2.zero, 0f, 1f, 0f, 0f, 0f, 0f, 0f,
                                             false, false, 0f, jointTorque2: 0.25f, jointVelocity2: 0.5f,
                                             actionRate2: 0.75f);
            Assert.AreEqual(0.25f, context.JointTorque2);
            Assert.AreEqual(0.5f, context.JointVelocity2);
            Assert.AreEqual(0.75f, context.ActionRate2);
        }
    }
}
