using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PoSumo.Tests
{
    /// PlayMode checks on the live arena (SCN_SUMO, the scene every bout is played in):
    /// the scene loads with no error, the body is built to the physics contract, every
    /// Po Game Standard reward penalty is actually charged while the trained policies
    /// fight, and the overlay's training-speed benchmark runs and hands the clock back.
    ///
    /// Nothing here writes a tracked file or the player's career: the career recorder
    /// is removed as soon as the match manager spawns it.
    public sealed class ArenaPlayTests
    {
        private const string ARENA = "SCN_SUMO";

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ARENA, LoadSceneMode.Single);
            yield return null;
            DropCareerRecorder();
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            Time.timeScale = 1f;
            yield return null;
        }

        private static void DropCareerRecorder()
        {
            foreach (Systems_CareerRecorder recorder in Object.FindObjectsByType<Systems_CareerRecorder>())
            {
                Object.DestroyImmediate(recorder.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator ArenaBuildsTwoFightersToThePhysicsContract()
        {
            yield return new WaitForSeconds(0.5f);
            Agent_Biped[] fighters = Object.FindObjectsByType<Agent_Biped>();
            Assert.That(fighters.Length, Is.GreaterThanOrEqualTo(2), "the arena seats two fighters");

            Assert.AreEqual(-9.81f, Physics2D.gravity.y, 1e-3f, "Earth gravity");
            Assert.AreEqual(0.02f, Time.fixedDeltaTime, 1e-6f, "fixed 50 Hz step");

            foreach (Agent_Biped fighter in fighters)
            {
                Agent_BipedBody body = fighter.GetComponent<Agent_BipedBody>();
                Assert.IsNotNull(body, fighter.name + " has a ragdoll body");
                HingeJoint2D[] joints = body.Joints;
                Assert.AreEqual(Agent_BipedBody.JointCount, joints.Length, fighter.name + ": every joint in JOINT_DEFS is built");
                int powered = 0;
                for (int jointIndex = 0; jointIndex < joints.Length; jointIndex++)
                {
                    HingeJoint2D joint = joints[jointIndex];
                    Assert.IsTrue(joint.useLimits, joint.name + " has anatomical limits");
                    Assert.Less(joint.limits.min, joint.limits.max, joint.name + " limit range");
                    Assert.IsFalse(joint.enableCollision, joint.name + ": connected bones do not collide");
                    Collider2D child = joint.GetComponent<Collider2D>();
                    Collider2D parent = joint.connectedBody != null ? joint.connectedBody.GetComponent<Collider2D>() : null;
                    if (child != null && parent != null)
                    {
                        Assert.IsTrue(Physics2D.GetIgnoreCollision(child, parent), joint.name + ": parent-child pair ignored");
                    }
                    if (Agent_BipedBody.JointPowered(jointIndex))
                    {
                        powered++;
                        Assert.IsTrue(joint.useMotor, joint.name + " is actuated");
                        Assert.IsTrue(float.IsFinite(joint.motor.maxMotorTorque), joint.name + " has a finite torque budget");
                        Assert.That(joint.motor.maxMotorTorque, Is.InRange(1f, 2000f), joint.name + " torque is physiological");
                    }
                }
                Assert.AreEqual(Agent_Biped.MotorCount, powered, fighter.name + ": one actuated joint per motor action");

                bool footFriction = false;
                foreach (Collider2D collider in fighter.GetComponentsInChildren<Collider2D>())
                {
                    PhysicsMaterial2D material = collider.sharedMaterial;
                    if (material != null && material.friction >= 0.8f)
                    {
                        footFriction = true;
                    }
                }
                Assert.IsTrue(footFriction, fighter.name + ": feet carry a high-friction material");
            }
        }

        [UnityTest]
        public IEnumerator EveryRewardPenaltyTermIsChargedWhileThePoliciesFight()
        {
            Reward_SumoObjective.PenaltyTotals sum = default;
            float deadline = Time.realtimeSinceStartup + 120f;
            bool armed = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                DropCareerRecorder();
                sum = default;
                foreach (Agent_Biped fighter in Object.FindObjectsByType<Agent_Biped>())
                {
                    Reward_SumoObjective objective = fighter.SumoObjective;
                    // Weight 1 on every term (the shipped sheets keep them at 0 until a
                    // brain is retrained with them). Re-applied every frame because each
                    // episode start re-reads the sheet / trainer values.
                    objective.OverridePenalties(1f, 1f, 1f);
                    objective.OverrideSmoothness(1f, objective.SheetJerk2Penalty);
                    if (!armed)
                    {
                        objective.ResetPenaltyTotals();
                    }
                    Reward_SumoObjective.PenaltyTotals p = objective.Penalties;
                    sum.JointTorque += p.JointTorque;
                    sum.JointVelocity += p.JointVelocity;
                    sum.ActionRate += p.ActionRate;
                    sum.Slip += p.Slip;
                    sum.Steps += p.Steps;
                }
                armed = true;
                if (sum.Steps > 200 && sum.JointTorque < 0f && sum.JointVelocity < 0f && sum.ActionRate < 0f && sum.Slip < 0f)
                {
                    break;
                }
                yield return null;
            }
            Debug.Log($"[PENALTY] steps {sum.Steps} jointTorque {sum.JointTorque:0.000} jointVelocity {sum.JointVelocity:0.000} " +
                      $"actionRate {sum.ActionRate:0.000} footSlip {sum.Slip:0.0000}");
            Assert.That(sum.Steps, Is.GreaterThan(200), "the fighters ran sumo-school steps");
            Assert.Less(sum.JointTorque, 0f, "joint torque^2 penalty charged");
            Assert.Less(sum.JointVelocity, 0f, "joint angular velocity^2 penalty charged");
            Assert.Less(sum.ActionRate, 0f, "action rate (jitter) penalty charged");
            Assert.Less(sum.Slip, 0f, "foot sliding penalty charged");
        }

        [UnityTest]
        public IEnumerator BenchmarkTimesTheLiveArenaAndHandsTheClockBack()
        {
            yield return new WaitForSeconds(0.5f);
            var host = new GameObject("BenchmarkUnderTest");
            Systems_SimBenchmark benchmark = host.AddComponent<Systems_SimBenchmark>();
            yield return null;
            float scaleBefore = Time.timeScale;
            float maxDeltaBefore = Time.maximumDeltaTime;
            Assert.IsTrue(benchmark.Begin(), benchmark.Result);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (benchmark.IsRunning && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Debug.Log("[BENCH] " + benchmark.Result);
            Assert.IsFalse(benchmark.IsRunning, "the benchmark is bounded");
            Assert.Greater(benchmark.TicksPerSecond, 50f, "faster than real time (50 ticks/s)");
            Assert.Greater(benchmark.EnvStepsPerSecond, 0f, "ML-Agents environment steps were counted");
            StringAssert.Contains("not a training run", benchmark.Result);
            Assert.AreEqual(maxDeltaBefore, Time.maximumDeltaTime, 1e-6f, "maximumDeltaTime restored");
            Assert.That(Time.timeScale, Is.LessThanOrEqualTo(Mathf.Max(1f, scaleBefore)), "time scale handed back");
            Object.Destroy(host);
        }
    }
}
