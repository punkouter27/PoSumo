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
            Assert.IsNotNull(settings, "Assets/_Project/Data/Resources/DemoModeSettings.asset is missing");
            Assert.AreEqual(30f, settings.idleSeconds, 1e-4f);
            Assert.That(settings.cameraDwellSeconds, Is.InRange(6f, 8f));
            Assert.That(settings.cameraShots, Is.Not.Empty);
            Assert.IsNotNull(settings.overlayPanelSettings, "the overlay needs the game's PanelSettings");
        }

        [Test]
        public void DemoIdleTimerIgnoresLongFrames()
        {
            // Ordinary 60 fps frames count toward the 30 s idle threshold...
            float idle = 0f;
            for (int frame = 0; frame < 60; frame++)
            {
                idle = Systems_DemoMode.AccumulateIdle(idle, 1f / 60f, 0.25f);
            }
            Assert.AreEqual(1f, idle, 1e-3f);
            // ...a 70 s shader warm-up frame, a background tab or a NaN does not.
            Assert.AreEqual(idle, Systems_DemoMode.AccumulateIdle(idle, 70f, 0.25f));
            Assert.AreEqual(idle, Systems_DemoMode.AccumulateIdle(idle, 0.3f, 0.25f));
            Assert.AreEqual(idle, Systems_DemoMode.AccumulateIdle(idle, float.NaN, 0.25f));
            Assert.AreEqual(idle, Systems_DemoMode.AccumulateIdle(idle, -1f, 0.25f));
        }

        [Test]
        public void DemoIdleTimerNeverStartsTheDemoFromStallsAlone()
        {
            // 100 one-second stalls (a tab in the background) add up to nothing.
            float idle = 0f;
            for (int stall = 0; stall < 100; stall++)
            {
                idle = Systems_DemoMode.AccumulateIdle(idle, 1f, 0.25f);
            }
            Assert.AreEqual(0f, idle);
        }

        [Test]
        public void DemoCameraDwellSkipsALongFrame()
        {
            float shot = 0f;
            // A 5 s hitch does not end a 7 s shot...
            Assert.IsFalse(Systems_DemoMode.ShotDue(ref shot, 5f, 0.25f, 7f));
            Assert.AreEqual(0f, shot);
            // ...7 s of ordinary 0.1 s frames does, and the clock restarts.
            bool due = false;
            for (int frame = 0; frame < 69 && !due; frame++)
            {
                due = Systems_DemoMode.ShotDue(ref shot, 0.1f, 0.25f, 7f);
            }
            Assert.IsFalse(due, "6.9 s is not yet 7 s");
            Assert.IsTrue(Systems_DemoMode.ShotDue(ref shot, 0.1f + 1e-4f, 0.25f, 7f));
            Assert.AreEqual(0f, shot);
        }

        [Test]
        public void BenchmarkNamesTheHardwareAndWhatItMeasured()
        {
            string hardware = Systems_SimBenchmark.Hardware();
            StringAssert.Contains("CPU ", hardware);
            StringAssert.Contains("GPU ", hardware);
            string label = Systems_SimBenchmark.Describe(1000f, 50f, 33f, 2, 3f, hardware);
            StringAssert.Contains(hardware, label);
            StringAssert.Contains("Measured:", label);
            StringAssert.Contains("not a training run", label);
        }

        [Test]
        public void InferenceLogPrintsOneLinePerBrain()
        {
            Systems_InferenceLog.Clear();
            try
            {
                Assert.AreEqual("[PoSumo] Kim.onnx inference running (x)", Systems_InferenceLog.Line("Kim", "x"));
                UnityEngine.TestTools.LogAssert.Expect(LogType.Log, Systems_InferenceLog.Line("Kim", "a"));
                Assert.IsTrue(Systems_InferenceLog.Note("Kim", "a"));
                Assert.IsFalse(Systems_InferenceLog.Note("Kim", "b"), "the second decision of a brain logs nothing");
                UnityEngine.TestTools.LogAssert.Expect(LogType.Log, Systems_InferenceLog.Line("Matt", "a"));
                Assert.IsTrue(Systems_InferenceLog.Note("Matt", "a"));
                Assert.IsFalse(Systems_InferenceLog.Note(null, "a"));
            }
            finally
            {
                Systems_InferenceLog.Clear();
            }
        }

        [Test]
        public void EachRewardPenaltyTermIsLoggedUnderItsOwnKey()
        {
            var start = new Reward_SumoObjective.PenaltyTotals { Effort = -1f, Steps = 10 };
            var end = new Reward_SumoObjective.PenaltyTotals
            {
                Effort = -3f, Jerk = -0.5f, Jerk2 = -0.25f, Slip = -0.125f,
                JointTorque = -2f, JointVelocity = -4f, ActionRate = -8f, Steps = 20,
            };
            var logged = new System.Collections.Generic.Dictionary<string, float>();
            int count = Reward_SumoObjective.PenaltyStats(start, end, (key, value) => logged.Add(key, value));
            Assert.AreEqual(7, count);
            Assert.AreEqual(7, logged.Count, "one key per term, no key shared");
            // Mean per step over the episode (10 steps here).
            Assert.AreEqual(-0.2f, logged["Penalty/Effort"], 1e-6f);
            Assert.AreEqual(-0.05f, logged["Penalty/Jerk"], 1e-6f);
            Assert.AreEqual(-0.025f, logged["Penalty/Jerk2"], 1e-6f);
            Assert.AreEqual(-0.0125f, logged["Penalty/FootSlip"], 1e-6f);
            Assert.AreEqual(-0.2f, logged["Penalty/JointTorque2"], 1e-6f);
            Assert.AreEqual(-0.4f, logged["Penalty/JointVelocity2"], 1e-6f);
            Assert.AreEqual(-0.8f, logged["Penalty/ActionRate2"], 1e-6f);
            // An episode with no reward steps logs nothing.
            Assert.AreEqual(0, Reward_SumoObjective.PenaltyStats(end, end, (key, value) => Assert.Fail(key)));
        }

        [Test]
        public void DemoModeSettingsCarryTheLongFrameCapAndBenchmarkTuning()
        {
            var settings = Resources.Load<Systems_DemoModeSettings>(Systems_DemoModeSettings.RESOURCE_PATH);
            Assert.IsNotNull(settings);
            Assert.That(settings.maxCountedFrameSeconds, Is.InRange(0.05f, 1f));
            Assert.That(settings.benchmarkSeconds, Is.InRange(0.5f, 10f), "the benchmark must stay short and bounded");
            Assert.That(settings.benchmarkTimeScale, Is.GreaterThan(1f));
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
