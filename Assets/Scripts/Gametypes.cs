// ============================================================
//  GameTypes.cs
//  Shared enums and data. TubeType order matches your physical
//  test tube colors exactly.
// ============================================================
using System.Collections.Generic;

namespace WhocastThat
{
    public enum TubeType
    {
        Hex          = 0,  // brown
        Tribute      = 1,  // yellow
        Dispel       = 2,  // red
        Foresight    = 3,  // pink
        Warp         = 4,  // grey
        Phase        = 5,  // green
        Reflection   = 6,  // blue
        Counterspell = 7,  // cyan
        Curse        = 8   // dark purple
    }

    // One logical card / test tube
    public class TubeData
    {
        public int      TubeId;
        public TubeType Type;

        public TubeData(int id, TubeType type)
        {
            TubeId = id;
            Type   = type;
        }

        public override string ToString() => $"{Type} (#{TubeId})";
    }

    // The player's current hand and state
    public class PlayerHand
    {
        public List<TubeData> Tubes        = new();
        public bool           IsAlive      = true;
        public bool           SkipNextDraw = false;   // Phase self-effect flag

        public bool     HasType(TubeType t)        => Tubes.Exists(h => h.Type == t);
        public TubeData FirstOfType(TubeType t)    => Tubes.Find(h => h.Type == t);

        public void Add(TubeData t)    => Tubes.Add(t);
        public void Remove(TubeData t) => Tubes.Remove(t);
    }
}