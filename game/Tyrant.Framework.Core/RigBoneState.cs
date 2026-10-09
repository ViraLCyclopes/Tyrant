namespace Tyrant.Framework.Core
{
    /// <summary>
    /// One edited bone between frames: the offset goes onto what the animator wrote, exactly once. A channel the animator did
    /// not write this frame still holds last frame's composed value; composing on that again stacks the offset every frame (the
    /// Nursery preview turns its bones but never resets their positions, so a moved bone flew away). Such a channel starts from
    /// the value it had before last frame's offset instead.
    /// </summary>
    public sealed class RigBoneState
    {
        private bool _hasFinal, _hasBase;
        private RigVector3 _finalPosition, _finalScale, _basePosition, _baseScale;
        private RigQuaternion _finalRotation, _baseRotation;

        /// <summary>
        /// Composes the offset onto the bone's current values. False (values unchanged) when the animator wrote nothing since
        /// last frame's end: the bone still wears the edit.
        /// </summary>
        public bool Compose(RigOffset offset, ref RigVector3 position, ref RigQuaternion rotation, ref RigVector3 scale)
        {
            var samePosition = _hasFinal && position == _finalPosition;
            var sameRotation = _hasFinal && rotation == _finalRotation;
            var sameScale = _hasFinal && scale == _finalScale;
            if (samePosition && sameRotation && sameScale) return false;
            if (_hasBase)
            {
                if (samePosition) position = _basePosition;
                if (sameRotation) rotation = _baseRotation;
                if (sameScale) scale = _baseScale;
            }
            (_basePosition, _baseRotation, _baseScale, _hasBase) = (position, rotation, scale, true);
            offset.Apply(ref position, ref rotation, ref scale);
            return true;
        }

        /// <summary>What the bone ends the frame as (after the game's write-back): next frame's "not written" test.</summary>
        public void NoteFinal(RigVector3 position, RigQuaternion rotation, RigVector3 scale)
        {
            (_finalPosition, _finalRotation, _finalScale, _hasFinal) = (position, rotation, scale, true);
        }
    }
}
