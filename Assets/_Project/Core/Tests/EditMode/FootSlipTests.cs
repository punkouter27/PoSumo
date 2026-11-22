using NUnit.Framework;
using PoSumo;

namespace PoSumo.Tests
{
    /// EditMode tests for `Reward_FootSlip.Measure`, the loaded-foot slip measure
    /// behind slipPenalty / walkSlipPenalty and Realism/FootSlip.
    ///
    /// Covers the arithmetic only. Whether the feet's load and velocity are
    /// sampled correctly off a live ragdoll is behavioural and is not tested here.
    public sealed class FootSlipTests
    {
        [Test]
        public void SwingFoot_WithNoLoad_IsFree()
        {
            Assert.AreEqual(0f, Reward_FootSlip.Measure(0f, 3f, 0f, -2f), 1e-6f);
        }

        [Test]
        public void PlantedStillFeet_AreFree()
        {
            Assert.AreEqual(0f, Reward_FootSlip.Measure(0.5f, 0f, 0.5f, 0f), 1e-6f);
        }

        [Test]
        public void LoadedSlidingFoot_IsChargedItsSpeed()
        {
            // Half the body on a foot counts as fully loaded.
            Assert.AreEqual(0.4f, Reward_FootSlip.Measure(0.5f, -0.4f, 0f, 0f), 1e-6f);
        }

        [Test]
        public void PartLoad_ScalesTheCharge()
        {
            Assert.AreEqual(0.2f, Reward_FootSlip.Measure(0.25f, 0.4f, 0f, 0f), 1e-6f);
        }

        [Test]
        public void BothFeet_Add()
        {
            Assert.AreEqual(0.3f, Reward_FootSlip.Measure(1f, 0.1f, 1f, -0.2f), 1e-6f);
        }

        [Test]
        public void NonFiniteInputs_AreSanitised()
        {
            Assert.AreEqual(0f, Reward_FootSlip.Measure(float.NaN, 1f, 1f, float.PositiveInfinity), 1e-6f);
        }
    }
}
