using UnityEngine.Timeline;

namespace PoSumo
{
    /// The tachiai ceremony's one Timeline track: a row of camera beats
    /// (`Systems_CeremonyClip`). Built in code by `Systems_TachiaiCeremony` — the
    /// project has no `.playable` assets and keeps it that way, so the timing lives
    /// beside the referee's countdown length instead of in an asset that could drift
    /// from it.
    [TrackClipType(typeof(Systems_CeremonyClip))]
    public sealed class Systems_CeremonyTrack : TrackAsset
    {
    }
}
