using System;
using System.Collections.Generic;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     Client-side queue of <see cref="PlayerEvent"/>s waiting to be sent.
    /// </summary>
    /// <remarks>
    ///     Bounded: the oldest are dropped first and the drop count travels with the next batch. A new
    ///     match generation discards events from the previous one into the same count. Not thread-safe;
    ///     used only from Unity's main thread.
    /// </remarks>
    public sealed class PlayerEventLog
    {
        /// <summary>
        ///     How many events are held before the oldest is dropped; minutes of backlog at normal rates.
        /// </summary>
        public const int DefaultCapacity = 200;

        private readonly Queue<PlayerEvent> pending;
        private readonly int capacity;

        private int generation;
        private int lost;

        public PlayerEventLog(int capacity = DefaultCapacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity,
                    "A queue that holds nothing loses everything.");
            }

            this.capacity = capacity;
            pending = new Queue<PlayerEvent>(capacity);
        }

        /// <summary>How many are waiting to be sent.</summary>
        public int Count => pending.Count;

        /// <summary>
        ///     Queues one event under the match generation it happened in; a new generation discards
        ///     the previous one's pending events.
        /// </summary>
        public void Add(int matchGeneration, PlayerEvent happened)
        {
            if (matchGeneration != generation)
            {
                lost += pending.Count;
                pending.Clear();
                generation = matchGeneration;
            }

            if (pending.Count == capacity)
            {
                pending.Dequeue();
                lost++;
            }

            pending.Enqueue(happened);
        }

        /// <summary>
        ///     Takes everything waiting as one batch and empties the queue. No retry copy is kept:
        ///     Nitrox packets are <c>RELIABLE_ORDERED</c>, so a resend would duplicate events.
        /// </summary>
        public bool Take(out PlayerEventBatch batch)
        {
            batch = null!;

            if (pending.Count == 0)
            {
                // A batch needs at least one event; the loss count rides on the next one.
                return false;
            }

            List<PlayerEvent> taken = new List<PlayerEvent>(pending.Count);
            while (pending.Count > 0)
            {
                taken.Add(pending.Dequeue());
            }

            batch = new PlayerEventBatch(generation, taken, lost);
            lost = 0;

            return true;
        }
    }
}
