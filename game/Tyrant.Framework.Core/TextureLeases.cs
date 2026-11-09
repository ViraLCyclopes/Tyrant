using System;
using System.Collections.Generic;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// Which loaded mod textures are still shown, so the rest can be freed: each user (an animal) holds the textures it wears
    /// now, and a texture nobody has held or asked for during a grace time is handed back for destruction. A texture that is
    /// pinned (one put on the game's own materials, whose users cannot be followed) is never handed back. Times are seconds
    /// on any steady clock.
    /// </summary>
    public sealed class TextureLeases
    {
        private readonly Dictionary<int, HashSet<string>> _held = new Dictionary<int, HashSet<string>>();
        private readonly Dictionary<string, int> _holders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _lastUsed = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The users that hold anything (to check which of them are gone).</summary>
        public IReadOnlyCollection<int> Users => _held.Keys;

        /// <summary>The texture was loaded or asked for now: its grace time starts again.</summary>
        public void Touch(string key, double now) => _lastUsed[key] = now;

        /// <summary>The texture is never freed.</summary>
        public void Pin(string key) => _pinned.Add(key);

        /// <summary>The textures this user wears now; whatever it held before and no longer wears is let go.</summary>
        public void Hold(int user, IReadOnlyCollection<string> keys, double now)
        {
            _held.TryGetValue(user, out var before);
            if (keys.Count == 0)
            {
                if (before != null) Release(user, now);
                return;
            }
            var next = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in next)
            {
                _lastUsed[key] = now;
                if (before != null && before.Contains(key)) continue;
                _holders.TryGetValue(key, out var count);
                _holders[key] = count + 1;
            }
            if (before != null)
                foreach (var key in before)
                    if (!next.Contains(key)) LetGo(key, now);
            _held[user] = next;
        }

        /// <summary>The user is gone (or wears no mod texture any more).</summary>
        public void Release(int user, double now)
        {
            if (!_held.TryGetValue(user, out var keys)) return;
            _held.Remove(user);
            foreach (var key in keys) LetGo(key, now);
        }

        private void LetGo(string key, double now)
        {
            _lastUsed[key] = now; // the grace time counts from the moment the last holder let go
            if (!_holders.TryGetValue(key, out var count)) return;
            if (count <= 1) _holders.Remove(key);
            else _holders[key] = count - 1;
        }

        /// <summary>
        /// The textures to destroy now: not pinned, held by nobody, and neither held nor asked for during the last
        /// <paramref name="grace"/> seconds. They are forgotten here; asking for one again loads it again.
        /// </summary>
        public List<string> Collect(double now, double grace)
        {
            var unused = new List<string>();
            foreach (var pair in _lastUsed)
                if (now - pair.Value >= grace && !_holders.ContainsKey(pair.Key) && !_pinned.Contains(pair.Key)) unused.Add(pair.Key);
            foreach (var key in unused) _lastUsed.Remove(key);
            return unused;
        }
    }
}
