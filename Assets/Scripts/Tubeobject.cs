// ============================================================
//  TubeObject.cs
//
//  Attach to every physical test tube prefab (or it's added
//  automatically by PotInteraction at spawn time).
//
//  Holds the TubeData so TablePlayZone knows which ability
//  to fire when the tube is set down on the table.
// ============================================================
using UnityEngine;

namespace WhocastThat
{
    public class TubeObject : MonoBehaviour
    {
        // Set automatically by PotInteraction when the tube spawns
        public TubeData Data;
    }
}