using UnityEngine;

namespace AlternativeGameInterface
{
    // Debug only: two do-nothing components stamp the time of the first Update and of the last LateUpdate of every frame (execution order -32000 / +32000), so that a long
    // frame can be split into "before the scripts", "the scripts before the mod's Update", "after it" - see Prof.Frame
    [DefaultExecutionOrder(-32000)]
    sealed class FrameStampFirst : MonoBehaviour { void Update() { Prof.MarkFirst(); } }

    [DefaultExecutionOrder(32000)]
    sealed class FrameStampLast : MonoBehaviour { void LateUpdate() { Prof.MarkLast(); } }
}
