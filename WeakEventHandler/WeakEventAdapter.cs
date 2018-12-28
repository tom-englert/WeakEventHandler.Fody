namespace WeakEventHandler
{
    using System;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.Threading;

    using JetBrains.Annotations;

    [GeneratedCode("WeakEventHandler.Fody", "1.0")]
    internal class WeakEventAdapter<TSource, TTarget, TEventArgs>
        where TEventArgs : EventArgs
        where TSource : class
        where TTarget : class
    {
        /// <summary>
        /// WeakReference to the object listening for the event.
        /// </summary>
        [NotNull]
        private readonly WeakReference<TTarget> _weakTarget;

        [NotNull]
        private readonly Action<TTarget, object, TEventArgs> _targetDelegate;

        [NotNull]
        private readonly Action<TSource, EventHandler<TEventArgs>> _addDelegate;

        [NotNull]
        private readonly Action<TSource, EventHandler<TEventArgs>> _removeDelegate;

        [NotNull, ItemNotNull]
        private List<TSource> _subscriptions = new List<TSource>();

        [NotNull]
        private readonly EventHandler<TEventArgs> _eventDelegate;

        public WeakEventAdapter(TTarget targetObject, [NotNull] Action<TTarget, object, TEventArgs> targetDelegate, [NotNull] Action<TSource, EventHandler<TEventArgs>> addDelegate, [NotNull] Action<TSource, EventHandler<TEventArgs>> removeDelegate)
        {
            _weakTarget = new WeakReference<TTarget>(targetObject);
            _targetDelegate = targetDelegate;
            _addDelegate = addDelegate;
            _removeDelegate = removeDelegate;
            _eventDelegate = OnEvent;
        }

        private void OnEvent(object sender, TEventArgs e)
        {
            if (!_weakTarget.TryGetTarget(out var target))
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

            _addDelegate(source, _eventDelegate);
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

            _removeDelegate(source, _eventDelegate);
        }

        public void Release()
        {
            foreach (var subscription in _subscriptions)
            {
                _removeDelegate(subscription, _eventDelegate);
            }
        }

#if NET40

        private static class Volatile
        {
            public static T Read<T>(ref T value)
            {
                return value;
            }
        }

        private class WeakReference<T> : WeakReference
            where T : class
        {
            public WeakReference(T target) 
                : base(target)
            {
            }

            public bool TryGetTarget(out T target)
            {
                target = Target as T;
                return target != null;
            }
        }

#endif
    }
}