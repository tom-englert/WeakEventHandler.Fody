namespace Template
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
            private readonly WeakEventAdapter<EventSource, EventTarget, EventArgs> _sourceEventAAdapter;
            [NotNull]
            private readonly WeakEventAdapter<EventSource, EventTarget, MyCancelEventArgs> _sourceEventBAdapter;
            [NotNull]
            private readonly WeakEventAdapter<EventSource, EventTarget, EventArgs> _sourceEventCAdapter;

            public EventTarget([NotNull] EventSource source, [NotNull] Action<string> eventTracer)
            {
                _source = source;
                _eventTracer = eventTracer;

                _sourceEventAAdapter = new WeakEventAdapter<EventSource, EventTarget, EventArgs>(this, GetStaticDelegate<EventArgs>(Source_EventA), Source_EventA_Add, Source_EventA_Remove);
                _sourceEventBAdapter = new WeakEventAdapter<EventSource, EventTarget, MyCancelEventArgs>(this, GetStaticDelegate<MyCancelEventArgs>(Source_EventB), Source_EventB_Add, Source_EventB_Remove);
                _sourceEventCAdapter = new WeakEventAdapter<EventSource, EventTarget, EventArgs>(this, GetStaticDelegate<EventArgs>(Source_EventA), Source_EventC_Add, Source_EventC_Remove);
            }

            public void Subscribe()
            {
                _sourceEventAAdapter.Subscribe(_source);
                _sourceEventBAdapter.Subscribe(_source);
                _sourceEventCAdapter.Subscribe(_source);
            }

            public void Unsubscribe()
            {
                _sourceEventAAdapter.Unsubscribe(_source);
                _sourceEventBAdapter.Unsubscribe(_source);
                _sourceEventCAdapter.Unsubscribe(_source);
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
                _sourceEventAAdapter.Release();
                _sourceEventBAdapter.Release();
                _sourceEventCAdapter.Release();
            }
        }
    }

    namespace Fody
    {
        using JetBrains.Annotations;

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
                GetSource(GetTarget(this)).EventA += Source_EventA;

                _source.EventB += Source_EventB;
                _source.EventC += Source_EventA;
            }

            public void Unsubscribe()
            {
                GetSource(GetTarget(this)).EventA -= Source_EventA;

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

            private static EventSource GetSource([NotNull] EventTarget target)
            {
                return target._source;
            }

            private static EventTarget GetTarget([NotNull] EventTarget target)
            {
                return target;
            }
        }
    }
}
