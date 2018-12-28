﻿namespace Template
{
    using System;
    using Common;

    public interface IEventTarget
    {
        void Subscribe();
        void Unsubscribe();
    }

    public enum TargetKind
    {
        Original,
        Weak,
        Fody
    }

    namespace Original
    {
        public class EventTarget : IEventTarget
        {
            private readonly EventSource _source;
            private readonly Action<string> _eventTracer;

            public EventTarget(EventSource source, Action<string> eventTracer)
            {
                _source = source;
                _eventTracer = eventTracer;
            }

            public void Subscribe()
            {
                _source.EventA += Source_EventA;
                _source.EventB += Source_EventB;
                _source.EventC += Source_EventA;
            }

            public void Unsubscribe()
            {
                _source.EventA -= Source_EventA;
                _source.EventB -= Source_EventB;
                _source.EventC -= Source_EventA;
            }

            private void Source_EventA(object sender, EventArgs e)
            {
                _eventTracer("EventA");
            }

            private void Source_EventB(object sender, MyCancelEventArgs e)
            {
                _eventTracer("EventB " + e.Cancel);
            }

            ~EventTarget()
            {

            }
        }
    }

    /*
     - Inject the WeakEventSource class
     - Replace the event fields with WeakEventSource fields
     - Inject initialization of the WeakEventSource fields into constructor
     - Replace code in all add_<Event> and remove_<Event> methods to call WeakEventSource.Subscribe/Unsubscribe
     - Replace all "ldfld Event" with "ldfld WeakEventSource"
     - Replace all "callvirt System.EventHandler`1<class [mscorlib]System.EventArgs>::Invoke" with "WeakEventSource`1<class [mscorlib]System.EventArgs>::Raise(object, !0/*class [mscorlib]System.EventArgs"
     */

    namespace Weak
    {
        using JetBrains.Annotations;

        using WeakEventHandler;

        public class EventTarget : IEventTarget
        {
            [NotNull]
            private readonly EventSource _source;
            [NotNull]
            private readonly Action<string> _eventTracer;

            private static void Source_EventA_Add([NotNull] EventSource source, EventHandler<EventArgs> handler)
            {
                source.EventA += handler;
            }
            private static void Source_EventA_Remove([NotNull] EventSource source, EventHandler<EventArgs> handler)
            {
                source.EventA -= handler;
            }
            private static void Source_EventB_Add([NotNull] EventSource source, EventHandler<MyCancelEventArgs> handler)
            {
                source.EventB += handler;
            }
            private static void Source_EventB_Remove([NotNull] EventSource source, EventHandler<MyCancelEventArgs> handler)
            {
                source.EventB -= handler;
            }
            private static void Source_EventC_Add([NotNull] EventSource source, EventHandler<EventArgs> handler)
            {
                source.EventC += handler;
            }
            private static void Source_EventC_Remove([NotNull] EventSource source, EventHandler<EventArgs> handler)
            {
                source.EventC -= handler;
            }

            [NotNull]
            private readonly WeakEventListener<EventSource, EventTarget, EventArgs> _source_EventA_Listener;
            [NotNull]
            private readonly WeakEventListener<EventSource, EventTarget, MyCancelEventArgs> _source_EventB_Listener;
            [NotNull]
            private readonly WeakEventListener<EventSource, EventTarget, EventArgs> _source_EventC_Listener;

            public EventTarget([NotNull] EventSource source, [NotNull] Action<string> eventTracer)
            {
                _source = source;
                _eventTracer = eventTracer;

                _source_EventA_Listener = new WeakEventListener<EventSource, EventTarget, EventArgs>(this, GetStaticDelegate<EventArgs>(Source_EventA), Source_EventA_Add, Source_EventA_Remove);
                _source_EventB_Listener = new WeakEventListener<EventSource, EventTarget, MyCancelEventArgs>(this, GetStaticDelegate<MyCancelEventArgs>(Source_EventB), Source_EventB_Add, Source_EventB_Remove);
                _source_EventC_Listener = new WeakEventListener<EventSource, EventTarget, EventArgs>(this, GetStaticDelegate<EventArgs>(Source_EventA), Source_EventC_Add, Source_EventC_Remove);
            }

            public void Subscribe()
            {
                _source_EventA_Listener.Subscribe(_source);
                _source_EventB_Listener.Subscribe(_source);
                _source_EventC_Listener.Subscribe(_source);
            }

            public void Unsubscribe()
            {
                _source_EventA_Listener.Unsubscribe(_source);
                _source_EventB_Listener.Unsubscribe(_source);
                _source_EventC_Listener.Unsubscribe(_source);
            }

            private void Source_EventA(object sender, EventArgs e)
            {
                _eventTracer("EventA");
            }

            private void Source_EventB(object sender, [NotNull] MyCancelEventArgs e)
            {
                _eventTracer("EventB " + e.Cancel);
            }

            [NotNull]
            private Action<EventTarget, object, T> GetStaticDelegate<T>([NotNull] Action<object, T> instanceDelegate)
                where T : EventArgs
            {
                return (Action<EventTarget, object, T>)Delegate.CreateDelegate(typeof(Action<EventTarget, object, T>), null, instanceDelegate.Method);
            }

            ~EventTarget()
            {
                _source_EventA_Listener.Release();
                _source_EventB_Listener.Release();
                _source_EventC_Listener.Release();
            }
        }
    }

    namespace Fody
    {
        public class EventTarget : IEventTarget
        {
            private readonly EventSource _source;
            private readonly Action<string> _eventTracer;

            public EventTarget(EventSource source, Action<string> eventTracer)
            {
                _source = source;
                _eventTracer = eventTracer;
            }

            public void Subscribe()
            {
                _source.EventA += Source_EventA;
                _source.EventB += Source_EventB;
                _source.EventC += Source_EventA;
            }

            public void Unsubscribe()
            {
                _source.EventA -= Source_EventA;
                _source.EventB -= Source_EventB;
                _source.EventC -= Source_EventA;
            }

            [WeakEventHandler.MakeWeak]
            private void Source_EventA(object sender, EventArgs e)
            {
                _eventTracer("EventA");
            }

            [WeakEventHandler.MakeWeak]
            private void Source_EventB(object sender, MyCancelEventArgs e)
            {
                _eventTracer("EventB " + e.Cancel);
            }
        }
    }
}
