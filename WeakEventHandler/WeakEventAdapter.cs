// ReSharper disable UnusedMember.Global
// ReSharper disable MemberCanBePrivate.Global
namespace WeakEventHandler
{
    using System;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
#if NETSTANDARD1_0
    using System.Reflection;
#endif
    using System.Threading;

    using JetBrains.Annotations;

    [GeneratedCode("WeakEventHandler.Fody", "1.0")]
    [UsedImplicitly]
    internal class WeakEventAdapter<TSource, TTarget, TEventArgs, TEventHandler>
        where TEventArgs : EventArgs
        where TEventHandler : Delegate
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
        private readonly Action<TSource, TEventHandler> _addDelegate;

        [NotNull]
        private readonly Action<TSource, TEventHandler> _removeDelegate;

        [NotNull, ItemNotNull]
        private List<TSource> _subscriptions = new List<TSource>();

        [NotNull]
        private readonly TEventHandler _eventDelegate;

        public WeakEventAdapter([NotNull] TTarget targetObject, [NotNull] Action<TTarget, object, TEventArgs> targetDelegate, [NotNull] Action<TSource, TEventHandler> addDelegate, [NotNull] Action<TSource, TEventHandler> removeDelegate)
        {
            _weakTarget = new WeakReference<TTarget>(targetObject);
            _targetDelegate = targetDelegate;
            _addDelegate = addDelegate;
            _removeDelegate = removeDelegate;

#if NETSTANDARD1_0
            var method = ((Action<object, TEventArgs>)OnEvent).GetMethodInfo();
            var type = typeof(TEventHandler);
            _eventDelegate = (TEventHandler)method.CreateDelegate(type, this);
#else
            var method = ((Action<object, TEventArgs>)OnEvent).Method;
            _eventDelegate = (TEventHandler)Delegate.CreateDelegate(typeof(TEventHandler), this, method);
#endif
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

            public bool TryGetTarget([CanBeNull] out T target)
            {
                target = Target as T;
                return target != null;
            }
        }

#endif
    }
}