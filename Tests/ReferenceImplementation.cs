namespace Template
{
    using System;
    using System.Reflection;

    using JetBrains.Annotations;

    public class MyCancelEventArgs : EventArgs
    {
        public MyCancelEventArgs(bool cancel)
        {
            Cancel = cancel;
        }

        public bool Cancel { get; }
    }

    public interface IEventSource
    {
        event EventHandler<EventArgs> EventA;
        event EventHandler<MyCancelEventArgs> EventB;
        event EventHandler<EventArgs> EventC;
    }

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

    public class EventSource : IEventSource
    {
        public event EventHandler<EventArgs> EventA;
        public event EventHandler<MyCancelEventArgs> EventB;
        public event EventHandler<EventArgs> EventC;

        public void OnEventA1()
        {
            EventA?.Invoke(this, EventArgs.Empty);
        }

        public void OnEventA2(EventArgs e)
        {
            EventA?.Invoke(this, e);
        }

        public void OnEventB(bool value)
        {
            EventB?.Invoke(this, new MyCancelEventArgs(value));
        }
    }

    namespace Original
    {
        public class EventTarget : IEventTarget
        {
            private readonly IEventSource _source;
            private readonly Action<string> _eventTracer;

            public EventTarget(IEventSource source, Action<string> eventTracer)
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
        class EventHelper<TSource, TEventArgs>
            where TEventArgs : EventArgs
        {
            public readonly EventInfo EventInfo;
            public readonly Action<TSource, EventHandler<TEventArgs>> AddMethod;
            public readonly Action<TSource, EventHandler<TEventArgs>> RemoveMethod;

            public EventHelper([NotNull] TSource source, [NotNull] string eventName)
            {
                var sourceType = source.GetType();

                EventInfo = sourceType.GetEvent(eventName);
                AddMethod = (Action<TSource, EventHandler<TEventArgs>>)Delegate.CreateDelegate(typeof(Action<TSource, EventHandler<TEventArgs>>), null, EventInfo.GetAddMethod());
                RemoveMethod = (Action<TSource, EventHandler<TEventArgs>>)Delegate.CreateDelegate(typeof(Action<TSource, EventHandler<TEventArgs>>), null, EventInfo.GetRemoveMethod());
            }
        }

        public class EventTarget<TSource> : IEventTarget where TSource : IEventSource
        {
            private readonly TSource _source;
            private readonly Action<string> _eventTracer;
            private readonly EventHelper<TSource, EventArgs> _sourceEventA;
            private readonly EventHelper<TSource, MyCancelEventArgs> _sourceEventB;
            private readonly EventHelper<TSource, EventArgs> _sourceEventC;

            public EventTarget([NotNull] TSource source, Action<string> eventTracer)
            {
                _source = source;
                _eventTracer = eventTracer;

                var sourceType = source.GetType();

                _sourceEventA = new EventHelper<TSource, EventArgs>(source, nameof(_source.EventA));
                _sourceEventB = new EventHelper<TSource, MyCancelEventArgs>(source, nameof(_source.EventB));
                _sourceEventC = new EventHelper<TSource, EventArgs>(source, nameof(_source.EventC));

                _source_EventA_Listener = new WeakEventListener<EventTarget<TSource>, EventArgs>(Source_EventA);
                _source_EventB_Listener = new WeakEventListener<EventTarget<TSource>, MyCancelEventArgs>(Source_EventB);
            }

            [NotNull]
            private readonly WeakEventListener<EventTarget<TSource>, EventArgs> _source_EventA_Listener;
            [NotNull]
            private readonly WeakEventListener<EventTarget<TSource>, MyCancelEventArgs> _source_EventB_Listener;

            public void Subscribe()
            {
                _source_EventA_Listener.Subscribe(_source, _sourceEventA.AddMethod, _sourceEventA.RemoveMethod);
                _source_EventB_Listener.Subscribe(_source, _sourceEventB.AddMethod, _sourceEventB.RemoveMethod);
                _source_EventA_Listener.Subscribe(_source, _sourceEventC.AddMethod, _sourceEventC.RemoveMethod);
            }

            public void Unsubscribe()
            {
                _source_EventA_Listener.Unsubscribe(_source, _sourceEventA.RemoveMethod);
                _source_EventB_Listener.Unsubscribe(_source, _sourceEventB.RemoveMethod);
                _source_EventA_Listener.Unsubscribe(_source, _sourceEventC.RemoveMethod);
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
                _source_EventA_Listener.Release();
                _source_EventB_Listener.Release();
            }
        }
    }

    namespace Fody
    {
        public class EventTarget : IEventTarget
        {
            private readonly IEventSource _source;
            private readonly Action<string> _eventTracer;

            public EventTarget(IEventSource source, Action<string> eventTracer)
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
