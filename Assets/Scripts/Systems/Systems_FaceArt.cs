using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace PoSumo
{
    /// The one place face sprites are looked up by name.
    ///
    /// The 21 face PNGs used to live in `Assets/Resources/Faces/` and were loaded
    /// with `Resources.Load<Sprite>("Faces/<name>")`. That defeated the atlas:
    /// **a sprite inside a Resources folder is never packed into a Sprite Atlas**
    /// (measured 2026-08-26 — `FaceAtlas` packed in 7 ms with `sprite.packed`
    /// false on every face, including the ones it listed), so every expression
    /// swap was its own texture and its own draw call.
    ///
    /// The PNGs now sit in `Assets/Art/Faces/` (same GUIDs — `git mv`), the atlas
    /// itself is the Resources asset (`Resources/Atlases/FaceAtlas`), and this
    /// hands out `SpriteAtlas.GetSprite(name)` results. Those are CLONES on every
    /// call, so they are cached: `Systems_FaceMood` asks for seven per fighter at
    /// Start and would otherwise leak a sprite per lookup.
    public static class Systems_FaceArt
    {
        private const string ATLAS_PATH = "Atlases/FaceAtlas";

        private static SpriteAtlas _atlas;
        private static bool _atlasLookedUp;
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// Domain reload is off in this project, so statics survive between Play
        /// sessions; clear the cache so a stale atlas from the last session is not
        /// handed out.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _atlas = null;
            _atlasLookedUp = false;
            Cache.Clear();
            NameCache.Clear();
        }

        /// A face sprite by bare name (e.g. "Nick_Happy_2"), or null if the atlas
        /// does not carry it. Never logs — callers decide whether a missing face
        /// is worth a warning (Systems_FaceMood counts what loaded).
        public static Sprite Load(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out Sprite cached)) return cached;

            if (!_atlasLookedUp)
            {
                _atlasLookedUp = true;
                _atlas = Resources.Load<SpriteAtlas>(ATLAS_PATH);
                if (_atlas == null)
                {
                    Debug.LogWarning($"Systems_FaceArt: no SpriteAtlas at Resources/{ATLAS_PATH} — faces will not load.");
                }
            }

            Sprite sprite = _atlas != null ? _atlas.GetSprite(name) : null;
            Cache[name] = sprite;   // null cached too: one miss, not one per frame
            return sprite;
        }

        /// Number of levels per mood. Shared with the voice so face level N and
        /// voice level N always mean the same intensity.
        public const int LEVELS = 5;

        /// The art for one expression, as far as it exists.
        ///
        /// Resolution order, and why:
        ///   1. `<prefix>_<Mood>_<level>` — real art for exactly this slot;
        ///   2. the NEAREST level of the same mood (ties go DOWN, the milder read) —
        ///      the trained fighters carry real photos at 1, 3 and 5 only;
        ///   3. `<prefix>_Neutral`, with `exact` false — the caller then draws the
        ///      mood procedurally on top (tint, squash, tilt).
        /// `levelOfArt` is the level the returned sprite was drawn AT (0 for the
        /// neutral), so a caller can draw only the remaining difference.
        public static Sprite Resolve(string prefix, Systems_FighterMood mood, int level,
                                     out int levelOfArt)
        {
            levelOfArt = 0;
            if (string.IsNullOrEmpty(prefix)) return null;
            if (mood != Systems_FighterMood.Neutral)
            {
                level = Mathf.Clamp(level, 1, LEVELS);
                for (int offset = 0; offset < LEVELS; offset++)
                {
                    int below = level - offset;
                    if (below >= 1)
                    {
                        Sprite sprite = Load(Name(prefix, mood, below));
                        if (sprite != null) { levelOfArt = below; return sprite; }
                    }
                    int above = level + offset;
                    if (offset > 0 && above <= LEVELS)
                    {
                        Sprite sprite = Load(Name(prefix, mood, above));
                        if (sprite != null) { levelOfArt = above; return sprite; }
                    }
                }
            }
            return Load(prefix + "_Neutral");
        }

        // Names are built once per (prefix, mood, level) and cached: FaceMood asks on
        // every expression step, and string concatenation per step is garbage.
        private const int MOOD_COUNT = 5;   // members of Systems_FighterMood
        private static readonly Dictionary<string, string[]> NameCache = new Dictionary<string, string[]>();

        private static string Name(string prefix, Systems_FighterMood mood, int level)
        {
            if (!NameCache.TryGetValue(prefix, out string[] names))
            {
                names = new string[MOOD_COUNT * (LEVELS + 1)];
                NameCache[prefix] = names;
            }
            int index = (int)mood * (LEVELS + 1) + level;
            string name = names[index];
            if (name == null)
            {
                name = $"{prefix}_{mood}_{level}";
                names[index] = name;
            }
            return name;
        }
    }
}
