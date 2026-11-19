namespace HandsAreHigh
{
    // Every roll the mod itself makes (nerve, reactions, decisions, gestures, voice picks) goes
    // through here instead of UnityEngine.Random. Normally it IS UnityEngine.Random. A debug
    // scenario launched with a seed switches it to a System.Random seeded with that value
    // until the scenario is cleaned up, so the mod's own sampled values are drawn from a
    // reproducible stream.
    //
    // What that does NOT make deterministic: the game (AI, ballistics, animation, frame timing)
    // and the order in which our rolls are asked for, which follows game events and frame
    // timing. Same seed + same situation gives the same per-bot values (nerve, reaction,
    // mercy) and usually the same early decisions; it never promises an identical replay.
    internal static class Dice
    {
        private static System.Random _seeded;
        internal static int Seed { get; private set; }
        internal static bool Seeded => _seeded != null;

        internal static void UseSeed(int seed)
        {
            Seed = seed;
            _seeded = new System.Random(seed);
        }

        internal static void Unseed()
        {
            _seeded = null;
            Seed = 0;
        }

        // [0, 1], as UnityEngine.Random.value.
        internal static float Value => _seeded != null ? (float)_seeded.NextDouble() : UnityEngine.Random.value;

        // [min, max], as UnityEngine.Random.Range(float, float).
        internal static float Range(float min, float max) =>
            _seeded != null ? min + (float)_seeded.NextDouble() * (max - min) : UnityEngine.Random.Range(min, max);

        // [min, max), as UnityEngine.Random.Range(int, int).
        internal static int Range(int min, int max) =>
            _seeded != null ? (max <= min ? min : _seeded.Next(min, max)) : UnityEngine.Random.Range(min, max);
    }
}
