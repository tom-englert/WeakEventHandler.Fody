﻿// ReSharper disable UnusedMember.Global
// ReSharper disable MemberCanBePrivate.Global
using System.Runtime.CompilerServices;

[assembly:InternalsVisibleTo("SmokeTest, PublicKey=00240000048000009400000006020000002400005253413100040000010001005F0D2831FD1DAD0752CB855B844B1356FBE634AC19B68478C5A015D871B3D0CA6398D9572473C9562BD11670F6726F3E1D48E19FFC69DAD0624E93072CD1AD3C7288A04592E76F7B0867A52351D6B748213D109BB076B36DC9A18B4E110BA2857762CEAB880771A7CA1E474A877A6CBB7405D716EC43348296D7EF8F37E4BA9B")]

namespace WeakEventHandler
{
    using System;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
#if NETSTANDARD1_0
    using System.Reflection;
#endif
    using System.Threading;

    [GeneratedCode("WeakEventHandler.Fody", "1.0")]
    internal class WeakEventHandlerFodyWeakEventAdapter<TSource, TTarget, TEventArgs, TEventHandler>
        where TEventArgs : EventArgs
        where TEventHandler : Delegate
        where TSource : class
        where TTarget : class
    {
        /// <summary>
        /// WeakReference to the object listening for the event.
        /// </summary>
        private readonly WeakReference<TTarget> _weakTarget;

        private readonly Action<TTarget, object, TEventArgs> _targetDelegate;

        private readonly Action<TSource, TEventHandler> _addDelegate;

        private readonly Action<TSource, TEventHandler> _removeDelegate;

        private List<TSource> _subscriptions = new List<TSource>();

        private readonly TEventHandler _eventDelegate;

        public WeakEventHandlerFodyWeakEventAdapter(TTarget targetObject, Action<TTarget, object, TEventArgs> targetDelegate, Action<TSource, TEventHandler> addDelegate, Action<TSource, TEventHandler> removeDelegate)
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

        public void Subscribe(TSource source)
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

        public void Unsubscribe(TSource source)
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

            public bool TryGetTarget([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T target)
            {
                target = Target as T;
                return target != null;
            }
        }

#endif
    }
}