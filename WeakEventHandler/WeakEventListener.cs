namespace Template
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;

    using JetBrains.Annotations;

    internal static class Volatile
    {
        public static T Read<T>(ref T value)
        {
            // return Volatile.Read(value);
            return value;
        }
    }

    public class WeakEventListener<TTarget, TEventArgs> where TEventArgs : EventArgs
    {
        /// <summary>
        /// WeakReference to the object listening for the event.
        /// </summary>
        [NotNull]
        private readonly WeakReference _weakTarget;

        [NotNull]
        private readonly Action<TTarget, object, TEventArgs> _targetDelegate;

        [NotNull, ItemNotNull]
        private List<Subscription> _subscriptions = new List<Subscription>();

        [NotNull]
        private readonly EventHandler<TEventArgs> _eventDelegate;

        /// <summary>
        /// Initializes a new instances of the WeakEventListener class that references the source but not the target.
        /// </summary>
        public WeakEventListener(Action<object, TEventArgs> method)
            : this(method.Target, (Action<TTarget, object, TEventArgs>)Delegate.CreateDelegate(typeof(Action<TTarget, object, TEventArgs>), null, method.Method))
        {
        }

        public WeakEventListener(object targetObject, Action<TTarget, object, TEventArgs> targetDelegate)
        {
            _weakTarget = new WeakReference(targetObject);
            _targetDelegate = targetDelegate;
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

        public void Subscribe<T>([NotNull] T source, Action<T, EventHandler<TEventArgs>> add, Action<T, EventHandler<TEventArgs>> remove)
        {
            var subscription = new Subscription<T>(source, remove);

            var oldList = Volatile.Read(ref _subscriptions);

            while (true)
            {
                var newList = new List<Subscription>(oldList) { subscription };

                if (Interlocked.CompareExchange(ref _subscriptions, newList, oldList) == oldList)
                    break;

                oldList = Volatile.Read(ref _subscriptions);
            }

            add(source, _eventDelegate);
        }

        public void Unsubscribe<T>([NotNull] T source, Action<T, EventHandler<TEventArgs>> remove)
        {
            var oldList = Volatile.Read(ref _subscriptions);

            while (true)
            {
                var newList = new List<Subscription>(oldList);
                for (var i = 0; i < newList.Count; i++)
                {
                    if (!newList[i].Matches(source, remove.Method))
                        continue;

                    newList.RemoveAt(i);
                    break;
                }

                if (Interlocked.CompareExchange(ref _subscriptions, newList, oldList) == oldList)
                    break;

                oldList = Volatile.Read(ref _subscriptions);
            }

            remove(source, _eventDelegate);
        }

        public void Release()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.RemoveEventHandler(_eventDelegate);
            }
        }

        private abstract class Subscription
        {
            [NotNull]
            protected readonly object _source;
            private readonly MethodInfo  _remove;

            protected Subscription([NotNull] object source, MethodInfo remove)
            {
                _source = source;
                _remove = remove;
            }

            public bool Matches([NotNull] object source, MethodInfo remove)
            {
                return ReferenceEquals(source, _source) && (remove == _remove);
            }

            public abstract void RemoveEventHandler([NotNull] EventHandler<TEventArgs> eventDelegate);
        }

        private class Subscription<T> : Subscription
        {
            [NotNull]
            private readonly Action<T, EventHandler<TEventArgs>>  _remove;

            public Subscription([NotNull] T source, Action<T, EventHandler<TEventArgs>> remove)
                : base(source, remove.Method)
            {
                _remove = remove;
            }

            public override void RemoveEventHandler([NotNull] EventHandler<TEventArgs> eventDelegate)
            {
                _remove((T)_source, eventDelegate);
            }
        }
    }
}