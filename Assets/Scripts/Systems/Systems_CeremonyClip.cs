using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PoSumo
{
    /// One camera beat of the tachiai ceremony. The clip only carries WHICH beat;
    /// what each beat does lives in `Systems_TachiaiCeremony.ApplyBeat`, so the
    /// Timeline decides WHEN and the ceremony decides WHAT.
    public sealed class Systems_CeremonyClip : PlayableAsset, ITimelineClipAsset
    {
        public Systems_CeremonyBehaviour.Beat beat;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<Systems_CeremonyBehaviour>.Create(graph);
            Systems_CeremonyBehaviour behaviour = playable.GetBehaviour();
            behaviour.CurrentBeat = beat;
            behaviour.Owner = owner != null ? owner.GetComponent<Systems_TachiaiCeremony>() : null;
            return playable;
        }
    }
}
