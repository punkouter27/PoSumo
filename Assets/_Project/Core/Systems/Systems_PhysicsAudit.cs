using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace PoSumo
{
    /// Measures, during real bouts, how well the physics keeps bodies apart —
    /// so "eliminate clipping" is a number before anyone changes a collider.
    ///
    /// Every physics step while a round is live it asks Box2D for the signed
    /// distance (Collider2D.Distance) between:
    ///  - every part of fighter A and every part of fighter B   (agent vs agent),
    ///  - every part of each fighter and every solid arena collider (agent vs scene),
    ///  - every pair INSIDE one fighter that is allowed to collide — the 30
    ///    leg-vs-trunk pairs (self).
    /// A negative distance is penetration. It also tracks the fastest part's
    /// per-step travel against the thinnest collider (a tunnelling-risk ratio:
    /// above 1 means a part could cross a collider in one step without CCD), and
    /// the cost of Physics2D.Simulate when the profiler marker is available.
    ///
    /// Summary is logged as `PHYSICS AUDIT RESULT:` on every MatchEnded.
    ///
    /// READ-ONLY: it never moves, wakes or reconfigures a body, because every
    /// collider, mass and solver setting here is part of what the four brains
    /// were trained against. Spawned only in the Editor and development builds,
    /// behind `enablePhysicsAudit`. Cost: ~600 distance queries per step.
    public sealed class Systems_PhysicsAudit : MonoBehaviour
    {
        /// Penetration below this is solver slop (Box2D's linear slop is 5 mm),
        /// not a visible clip.
        private const float SLOP = 0.005f;
        private const float VISIBLE = 0.02f;

        private sealed class Lane
        {
            public string Name;
            public long PairSamples;
            public long Overlaps;
            public long Visible;
            public float MaxDepth;
            public double SumDepth;
            public string WorstPair = "-";

            public void Add(float distance, Collider2D a, Collider2D b)
            {
                PairSamples++;
                float depth = -distance;
                if (depth <= SLOP)
                {
                    return;
                }
                Overlaps++;
                SumDepth += depth;
                if (depth > VISIBLE)
                {
                    Visible++;
                }
                if (depth > MaxDepth)
                {
                    MaxDepth = depth;
                    WorstPair = a.name + "/" + b.name;
                }
            }

            public void Clear()
            {
                PairSamples = Overlaps = Visible = 0;
                MaxDepth = 0f;
                SumDepth = 0;
                WorstPair = "-";
            }

            public void Write(StringBuilder builder)
            {
                double mean = Overlaps > 0 ? SumDepth / Overlaps : 0.0;
                builder.Append(' ').Append(Name)
                       .Append("{pairs=").Append(PairSamples)
                       .Append(" overlap>5mm=").Append(Overlaps)
                       .Append(" visible>2cm=").Append(Visible)
                       .Append(" mean=").Append((mean * 1000.0).ToString("0.0")).Append("mm")
                       .Append(" max=").Append((MaxDepth * 1000f).ToString("0.0")).Append("mm")
                       .Append(" worst=").Append(WorstPair).Append('}');
            }
        }

        private readonly Lane _agentAgent = new Lane { Name = "agent-agent" };
        private readonly Lane _agentScene = new Lane { Name = "agent-scene" };
        private readonly Lane _self = new Lane { Name = "self" };

        private Systems_GameMatchManager _manager;
        private Agent_BipedBody _bodyA, _bodyB;
        private readonly List<Collider2D> _scene = new List<Collider2D>();
        private readonly List<KeyValuePair<Collider2D, Collider2D>> _selfPairsA = new List<KeyValuePair<Collider2D, Collider2D>>();
        private readonly List<KeyValuePair<Collider2D, Collider2D>> _selfPairsB = new List<KeyValuePair<Collider2D, Collider2D>>();
        private readonly StringBuilder _builder = new StringBuilder(512);
        private int _steps;

        // WHEN a deep (>2 cm) self overlap happens, by time since the round went
        // live — opening 0.6 s (the tachiai grace, while the body is still in or
        // leaving the shikiri crouch), 0.6-2 s, and after — and how far the
        // thigh's hip was folded at that moment. Added 2026-11-10 to test whether
        // the ThighNear/Chest overlap is the crouched start pose or the fight.
        private static readonly float[] TimeEdges = { 0.6f, 2f };
        private readonly int[] _deepByTime = new int[3];
        private readonly int[] _stepsByTime = new int[3];
        private readonly int[] _deepStepsByTime = new int[3];
        private int _deepHipFolded;     // hip jointAngle at or past -100 deg (flexion is negative)
        private int _deepHipOpen;
        private float _roundStartTime = -1f;
        private readonly StringBuilder _timing = new StringBuilder(256);
        private float _maxTunnelRatio;
        private float _minThickness = float.MaxValue;
        private ProfilerRecorder _simulate;
        private double _simulateSumMs;
        private double _simulateMaxMs;
        private int _simulateSamples;

        private void Start()
        {
            _manager = FindAnyObjectByType<Systems_GameMatchManager>();
            if (_manager == null || _manager.wrestlerA == null || _manager.wrestlerB == null)
            {
                enabled = false;
                return;
            }
            _bodyA = _manager.wrestlerA.GetComponent<Agent_BipedBody>();
            _bodyB = _manager.wrestlerB.GetComponent<Agent_BipedBody>();
            _manager.MatchEnded += OnMatchEnded;
            _manager.MatchReset += OnMatchReset;
            _manager.RoundStarted += OnRoundStarted;

            Systems_SumoArena arena = FindAnyObjectByType<Systems_SumoArena>();
            if (arena != null)
            {
                Collider2D[] colliders = arena.GetComponentsInChildren<Collider2D>();
                for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                {
                    if (!colliders[colliderIndex].isTrigger)
                    {
                        _scene.Add(colliders[colliderIndex]);
                    }
                }
            }

            BuildSelfPairs(_bodyA, _selfPairsA);
            BuildSelfPairs(_bodyB, _selfPairsB);
            MeasureThickness(_bodyA);
            MeasureThickness(_bodyB);

            _simulate = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics2D.Simulate");
        }

        private void OnDestroy()
        {
            if (_manager != null)
            {
                _manager.MatchEnded -= OnMatchEnded;
                _manager.MatchReset -= OnMatchReset;
                _manager.RoundStarted -= OnRoundStarted;
            }
            if (_simulate.Valid)
            {
                _simulate.Dispose();
            }
        }

        private static void BuildSelfPairs(Agent_BipedBody body, List<KeyValuePair<Collider2D, Collider2D>> pairs)
        {
            if (body == null)
            {
                return;
            }
            List<Collider2D> colliders = body.AllColliders;
            for (int first = 0; first < colliders.Count; first++)
            {
                for (int second = first + 1; second < colliders.Count; second++)
                {
                    Collider2D a = colliders[first];
                    Collider2D b = colliders[second];
                    if (a == null || b == null || a.isTrigger || b.isTrigger || a.attachedRigidbody == b.attachedRigidbody)
                    {
                        continue;
                    }
                    if (!Physics2D.GetIgnoreCollision(a, b))
                    {
                        pairs.Add(new KeyValuePair<Collider2D, Collider2D>(a, b));
                    }
                }
            }
        }

        private void MeasureThickness(Agent_BipedBody body)
        {
            if (body == null)
            {
                return;
            }
            for (int colliderIndex = 0; colliderIndex < body.AllColliders.Count; colliderIndex++)
            {
                Collider2D collider = body.AllColliders[colliderIndex];
                if (collider == null || collider.isTrigger)
                {
                    continue;
                }
                Vector3 size = collider.bounds.size;
                _minThickness = Mathf.Min(_minThickness, Mathf.Min(size.x, size.y));
            }
        }

        private void FixedUpdate()
        {
            if (_manager == null || !_manager.RoundActive || _bodyA == null || _bodyB == null)
            {
                return;
            }
            _steps++;
            if (_roundStartTime < 0f)
            {
                _roundStartTime = Time.time;   // first live step if RoundStarted came before we subscribed
            }
            _timeBucket = Bucket(Time.time - _roundStartTime);
            _stepsByTime[_timeBucket]++;
            _deepThisStep = false;

            List<Collider2D> partsA = _bodyA.AllColliders;
            List<Collider2D> partsB = _bodyB.AllColliders;
            for (int indexA = 0; indexA < partsA.Count; indexA++)
            {
                Collider2D a = partsA[indexA];
                if (!Usable(a))
                {
                    continue;
                }
                for (int indexB = 0; indexB < partsB.Count; indexB++)
                {
                    Collider2D b = partsB[indexB];
                    if (Usable(b))
                    {
                        Sample(_agentAgent, a, b);
                    }
                }
            }

            SampleScene(partsA);
            SampleScene(partsB);
            SampleSelf(_selfPairsA);
            SampleSelf(_selfPairsB);

            if (_deepThisStep)
            {
                _deepStepsByTime[_timeBucket]++;
            }

            TrackTunnelling(_bodyA);
            TrackTunnelling(_bodyB);

            if (_simulate.Valid && _simulate.LastValue > 0)
            {
                double ms = _simulate.LastValue / 1e6;
                _simulateSumMs += ms;
                _simulateMaxMs = System.Math.Max(_simulateMaxMs, ms);
                _simulateSamples++;
            }
        }

        private static bool Usable(Collider2D collider) =>
            collider != null && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy;

        private static void Sample(Lane lane, Collider2D a, Collider2D b)
        {
            ColliderDistance2D distance = a.Distance(b);
            if (distance.isValid)
            {
                lane.Add(distance.distance, a, b);
            }
        }

        private void SampleScene(List<Collider2D> parts)
        {
            for (int partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                Collider2D part = parts[partIndex];
                if (!Usable(part))
                {
                    continue;
                }
                for (int sceneIndex = 0; sceneIndex < _scene.Count; sceneIndex++)
                {
                    Collider2D solid = _scene[sceneIndex];
                    if (Usable(solid))
                    {
                        Sample(_agentScene, part, solid);
                    }
                }
            }
        }

        private void SampleSelf(List<KeyValuePair<Collider2D, Collider2D>> pairs)
        {
            for (int pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
            {
                Collider2D a = pairs[pairIndex].Key;
                Collider2D b = pairs[pairIndex].Value;
                if (!Usable(a) || !Usable(b))
                {
                    continue;
                }
                ColliderDistance2D distance = a.Distance(b);
                if (!distance.isValid)
                {
                    continue;
                }
                _self.Add(distance.distance, a, b);
                if (-distance.distance > VISIBLE)
                {
                    _deepThisStep = true;
                    _deepByTime[_timeBucket]++;
                    if (HipAngle(a, b) <= -100f)
                    {
                        _deepHipFolded++;
                    }
                    else
                    {
                        _deepHipOpen++;
                    }
                }
            }
        }

        private int _timeBucket;
        private bool _deepThisStep;

        private static int Bucket(float seconds)
        {
            for (int edgeIndex = 0; edgeIndex < TimeEdges.Length; edgeIndex++)
            {
                if (seconds < TimeEdges[edgeIndex])
                {
                    return edgeIndex;
                }
            }
            return TimeEdges.Length;
        }

        /// The hip jointAngle of whichever collider in the pair is a thigh — the
        /// hinge lives on the thigh's own rigidbody. 0 if neither is a thigh.
        private static float HipAngle(Collider2D a, Collider2D b)
        {
            Rigidbody2D thigh = a.name.StartsWith("Thigh") ? a.attachedRigidbody
                              : b.name.StartsWith("Thigh") ? b.attachedRigidbody : null;
            if (thigh == null)
            {
                return 0f;
            }
            HingeJoint2D hinge = thigh.GetComponent<HingeJoint2D>();
            return hinge != null ? hinge.jointAngle : 0f;
        }

        private void OnRoundStarted()
        {
            _roundStartTime = Time.time;
        }

        private void TrackTunnelling(Agent_BipedBody body)
        {
            if (body.Parts == null || _minThickness <= 0f || _minThickness == float.MaxValue)
            {
                return;
            }
            float dt = Time.fixedDeltaTime;
            for (int partIndex = 0; partIndex < body.Parts.Length; partIndex++)
            {
                Rigidbody2D part = body.Parts[partIndex];
                if (part != null)
                {
                    _maxTunnelRatio = Mathf.Max(_maxTunnelRatio, part.linearVelocity.magnitude * dt / _minThickness);
                }
            }
        }

        private void OnMatchEnded(Agent_Biped winner)
        {
            _builder.Clear();
            _builder.Append("PHYSICS AUDIT RESULT: steps=").Append(_steps)
                    .Append(" massA=").Append(_bodyA.TotalMass.ToString("0.0"))
                    .Append(" massB=").Append(_bodyB.TotalMass.ToString("0.0"))
                    .Append(" sceneColliders=").Append(_scene.Count)
                    .Append(" selfPairs=").Append(_selfPairsA.Count);
            _agentAgent.Write(_builder);
            _agentScene.Write(_builder);
            _self.Write(_builder);
            _builder.Append(" tunnelRisk=").Append(_maxTunnelRatio.ToString("0.00"))
                    .Append(" (thinnest ").Append((_minThickness * 100f).ToString("0.0")).Append("cm, CCD on)");
            if (_simulateSamples > 0)
            {
                _builder.Append(" simulate mean=").Append((_simulateSumMs / _simulateSamples).ToString("0.000"))
                        .Append("ms max=").Append(_simulateMaxMs.ToString("0.000")).Append("ms");
            }
            else
            {
                _builder.Append(" simulate=n/a");
            }
            Systems_Log.Info(_builder.ToString());

            _timing.Clear();
            _timing.Append("SELF OVERLAP TIMING: ");
            string[] labels = { "0-0.6s", "0.6-2s", ">2s" };
            for (int bucketIndex = 0; bucketIndex < labels.Length; bucketIndex++)
            {
                _timing.Append(labels[bucketIndex]).Append("{steps=").Append(_stepsByTime[bucketIndex])
                       .Append(" deepSteps=").Append(_deepStepsByTime[bucketIndex])
                       .Append(" deepPairs=").Append(_deepByTime[bucketIndex]).Append("} ");
            }
            _timing.Append("hipFolded(<=-100deg)=").Append(_deepHipFolded)
                   .Append(" hipOpen=").Append(_deepHipOpen);
            Systems_Log.Info(_timing.ToString());
        }

        private void OnMatchReset()
        {
            _agentAgent.Clear();
            _agentScene.Clear();
            _self.Clear();
            _steps = 0;
            System.Array.Clear(_deepByTime, 0, _deepByTime.Length);
            System.Array.Clear(_stepsByTime, 0, _stepsByTime.Length);
            System.Array.Clear(_deepStepsByTime, 0, _deepStepsByTime.Length);
            _deepHipFolded = 0;
            _deepHipOpen = 0;
            _roundStartTime = -1f;
            _maxTunnelRatio = 0f;
            _simulateSumMs = 0;
            _simulateMaxMs = 0;
            _simulateSamples = 0;
        }
    }
}
