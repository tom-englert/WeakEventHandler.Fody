namespace WeakEventHandler
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    using JetBrains.Annotations;

#if NET40
    internal static class Volatile
    {
        public static T Read<T>(ref T value)
        {
            return value;
        }
    }
#endif

    internal class WeakEventListener<TSource, TTarget, TEventArgs> where TEventArgs : EventArgs
        where TSource : class
    {
        /// <summary>
        /// WeakReference to the object listening for the event.
        /// </summary>
        [NotNull]
        private readonly WeakReference _weakTarget;

        [NotNull]
        private readonly Action<TTarget, object, TEventArgs> _targetDelegate;

        [NotNull]
        private readonly Action<TSource, EventHandler<TEventArgs>> _add;

        [NotNull]
        private readonly Action<TSource, EventHandler<TEventArgs>> _remove;

        [NotNull, ItemNotNull]
        private List<TSource> _subscriptions = new List<TSource>();

        [NotNull]
        private readonly EventHandler<TEventArgs> _eventDelegate;

        /// <summary>
        /// Initializes a new instances of the WeakEventListener class that references the source but not the target.
        /// </summary>
        public WeakEventListener(object targetObject, [NotNull] Action<TTarget, object, TEventArgs> targetDelegate, [NotNull] Action<TSource, EventHandler<TEventArgs>> add, [NotNull] Action<TSource, EventHandler<TEventArgs>> remove)
        {
            _weakTarget = new WeakReference(targetObject);
            _targetDelegate = targetDelegate;
            _add = add;
            _remove = remove;
            _eventDelegate = OnEvent;
        }

        private void OnEvent(object sender, TEventArgs e)
        {
            var target = (TTarget)_weakTarget.Target;

            if (target == null)
            {
                Release();
                return;
            }

            _targetDelegate(target, sender, e);
        }

        public void Subscribe([NotNull] TSource source)
        {
            var oldList = Volatile.Read(ref _subscriptions);

            while (true)
            {
                var newList = new List<TSource>(oldList) { source };

                if (Interlocked.CompareExchange(ref _subscriptions, newList, oldList) == oldList)
                    break;

                oldList = Volatile.Read(ref _subscriptions);
            }

            _add(source, _eventDelegate);
        }

        public void Unsubscribe([NotNull] TSource source)
        {
            var oldList = Volatile.Read(ref _subscriptions);

            while (true)
            {
                var newList = new List<TSource>(oldList);

                newList.Remove(source);

                if (Interlocked.CompareExchange(ref _subscriptions, newList, oldList) == oldList)
                    break;

                oldList = Volatile.Read(ref _subscriptions);
            }

            _remove(source, _eventDelegate);
        }

        public void Release()
        {
            foreach (var subscription in _subscriptions)
            {
                _remove(subscription, _eventDelegate);
            }
        }
    }
}