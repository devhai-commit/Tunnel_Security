using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Center.Models
{
    /// <summary>
    /// ObservableCollection capped at <see cref="MaxSize"/> items.
    /// When the cap is exceeded, the oldest items are dropped in one batch
    /// and a single Reset notification is fired — instead of N individual
    /// Remove notifications (which caused O(N) index shifts each tick).
    /// </summary>
    public sealed class BoundedObservableCollection<T> : ObservableCollection<T>
    {
        private readonly int _maxSize;
        private readonly int _trimTo;

        /// <param name="maxSize">Start trimming when count exceeds this.</param>
        /// <param name="trimTo">Keep this many items after trimming.</param>
        public BoundedObservableCollection(int maxSize = 500, int trimTo = 400)
        {
            _maxSize = maxSize;
            _trimTo  = trimTo;
        }

        public void AddBounded(T item)
        {
            Add(item);
            if (Count > _maxSize)
                TrimToSize();
        }

        private void TrimToSize()
        {
            int drop = Count - _trimTo;
            if (drop <= 0) return;

            // Mutate the underlying List directly — no per-item notifications.
            for (int i = 0; i < drop; i++)
                Items.RemoveAt(0);

            // Fire one Reset so ListView refreshes in a single pass.
            OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
