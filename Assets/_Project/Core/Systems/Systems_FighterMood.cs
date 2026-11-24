namespace PoSumo
{
    /// The expressive moods a fighter has, shared by its VOICE and its FACE so
    /// the two can never disagree. Each mood carries five levels, 1 = mildest and
    /// 5 = strongest; `Neutral` has no level and is internal (the resting face).
    ///
    /// The Po tournament standard (2026-11-22) is FIVE emotions x five levels:
    /// angry, happy, hurt, insult, celebrate. `Celebrate` is the newest member and
    /// is appended LAST so every existing ordinal (and the arrays indexed by it)
    /// keeps its meaning. Standard voice files are
    /// `Resources/Audio/Voice/<Fighter>/<emotion>-<level>.wav` (emotion lower-case,
    /// `Systems_FighterVoice.StandardName`); the legacy names below still resolve.
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
        /// Winning: a round (2-4) or the match (5). Its own high-energy synth
        /// profile; a fighter with no celebrate art shows its Happy face.
        Celebrate = 5,
    }
}
