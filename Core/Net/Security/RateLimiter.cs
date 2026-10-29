using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// A token bucket per key (a peer, usually): each key may act in bursts of up to <c>burst</c> and then
    /// at <c>perSecond</c>. Used to stop a flood of requests from using the host up, or the host's replies
    /// being used to flood somebody else.
    /// </summary>
    internal sealed class RateLimiter
    {
        private sealed class Bucket
        {
            public double Tokens;
            public double LastSeen;
        }

        private readonly double burst;
        private readonly double perSecond;
        private readonly int maxKeys;
        private readonly Func<double> seconds;
        private readonly Dictionary<string, Bucket> buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        private readonly object gate = new object();

        /// <param name="burst">How many actions a key may do at once.</param>
        /// <param name="perSecond">How many it may do per second after that.</param>
        /// <param name="maxKeys">How many keys to remember; the idlest are forgotten first, so a flood of
        /// made-up keys cannot grow this without limit.</param>
        /// <param name="seconds">The clock, for tests.</param>
        public RateLimiter(double burst, double perSecond, int maxKeys = 512, Func<double> seconds = null)
        {
            this.burst = burst;
            this.perSecond = perSecond;
            this.maxKeys = maxKeys;
            var clock = Stopwatch.StartNew();
            this.seconds = seconds ?? (() => clock.Elapsed.TotalSeconds);
        }

        /// <summary>True if the key may act now (and uses one token); false if it must wait.</summary>
        public bool TryAcquire(string key)
        {
            double now = seconds();
            lock (gate)
            {
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    if (buckets.Count >= maxKeys)
                        ForgetTheIdlest();
                    bucket = new Bucket { Tokens = burst, LastSeen = now };
                    buckets[key] = bucket;
                }
                else
                {
                    bucket.Tokens = Math.Min(burst, bucket.Tokens + (now - bucket.LastSeen) * perSecond);
                    bucket.LastSeen = now;
                }

                if (bucket.Tokens < 1)
                    return false;
                bucket.Tokens -= 1;
                return true;
            }
        }

        private void ForgetTheIdlest()
        {
            // the tenth of the keys that have been quiet longest
            foreach (var key in buckets.OrderBy(b => b.Value.LastSeen).Take(Math.Max(1, maxKeys / 10)).Select(b => b.Key).ToList())
                buckets.Remove(key);
        }

        /// <summary>How many keys are being remembered.</summary>
        public int TrackedKeys
        {
            get { lock (gate) return buckets.Count; }
        }
    }
}
