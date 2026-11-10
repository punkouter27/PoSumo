namespace PoSumo
{
    /// The four expressive moods a fighter has, shared by its VOICE and its FACE so
    /// the two can never disagree. Each mood carries five levels, 1 = mildest and
    /// 5 = strongest; `Neutral` has no level.
    ///
    /// Asset names follow this enum exactly — `<Name>_<Mood>_<Level>.png` for a face,
    /// `<Name>_<Mood>_L<Level>_<Variant>.wav` for a voice line — so the enum's
    /// `ToString()` IS the file-name segment. Renaming a member renames every asset
    /// it resolves; do not.
    public enum Systems_FighterMood
    {
        Neutral = 0,
        Happy = 1,
        Angry = 2,
        Hurt = 3,
        Insult = 4,
    }
}
