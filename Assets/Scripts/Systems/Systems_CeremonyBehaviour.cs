using UnityEngine.Playables;

namespace PoSumo
{
    /// Runtime half of a `Systems_CeremonyClip`. Re-issues its beat on EVERY frame
    /// the clip is active, not once on entry — a camera shot's deadline is in
    /// realtime while the Timeline runs on game time, and the two diverge across the
    /// arena scene load (the opening face zoom was measured silently not happening
    /// for exactly that reason; see TickIntroCountdown in Systems_GameMatchManager).
    public sealed class Systems_CeremonyBehaviour : PlayableBehaviour
    {
        public enum Beat
        {
            FaceA,
            FaceB,
            GazeLock,
            Release,
        }

        public Beat CurrentBeat;
        public Systems_TachiaiCeremony Owner;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (Owner != null)
            {
                Owner.ApplyBeat(CurrentBeat);
            }
        }
    }
}
