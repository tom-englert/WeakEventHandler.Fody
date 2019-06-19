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
        public class EventTarget<T> : IEventTarget
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

        public class EventTarget<T> : IEventTarget
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
            private readonly WeakEventAdapter<EventSource, EventTarget<T>, EventArgs> _sourceEventAAdapter;
            [NotNull]
            private readonly WeakEventAdapter<EventSource, EventTarget<T>, MyCancelEventArgs> _sourceEventBAdapter;
            [NotNull]
            private readonly WeakEventAdapter<EventSource, EventTarget<T>, EventArgs> _sourceEventCAdapter;

            public EventTarget([NotNull] EventSource source, [NotNull] Action<string> eventTracer)
            {
                _source = source;
                _eventTracer = eventTracer;

                _sourceEventAAdapter = new WeakEventAdapter<EventSource, EventTarget<T>, EventArgs>(this, GetStaticDelegate<EventArgs>(Source_EventA), Source_EventA_Add, Source_EventA_Remove);
                _sourceEventBAdapter = new WeakEventAdapter<EventSource, EventTarget<T>, MyCancelEventArgs>(this, GetStaticDelegate<MyCancelEventArgs>(Source_EventB), Source_EventB_Add, Source_EventB_Remove);
                _sourceEventCAdapter = new WeakEventAdapter<EventSource, EventTarget<T>, EventArgs>(this, GetStaticDelegate<EventArgs>(Source_EventA), Source_EventC_Add, Source_EventC_Remove);
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

            public void Subscribe2(EventSource source)
            {
                if (source == null)
                    return;
                
                _sourceEventAAdapter.Subscribe(source);
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
            private Action<EventTarget<T>, object, T1> GetStaticDelegate<T1>([NotNull] Action<object, T1> instanceDelegate)
                where T1 : EventArgs
            {
                return (Action<EventTarget<T>, object, T1>)Delegate.CreateDelegate(typeof(Action<EventTarget<T>, object, T1>), null, instanceDelegate.Method);
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

        public class EventTarget<T> : IEventTarget
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

            public void Subscribe2(EventSource source)
            {
                if (source == null)
                    return;

                source.EventA += Source_EventA;
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

            private static EventSource GetSource([NotNull] EventTarget<T> target)
            {
                return target._source;
            }

            private static EventTarget<T> GetTarget([NotNull] EventTarget<T> target)
            {
                return target;
            }
        }
    }
}
