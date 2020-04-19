// ReSharper disable all
namespace Template
{
    using System;
    using Common;
    using System.ComponentModel;

    public interface IEventTarget
    {
        void SubscribeEvents();
        void UnsubscribeEvents();

        void UnsubscribeAll();
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

            public void SubscribeEvents()
            {
                _source.EventA += Source_EventA;
                _source.EventB += Source_EventB;
                _source.EventC += Source_EventA;
                _source.PropertyChanged += Source_PropertyChanged;
            }

            public void UnsubscribeEvents()
            {
                _source.EventA -= Source_EventA;
                _source.EventB -= Source_EventB;
                _source.EventC -= Source_EventA;
                _source.PropertyChanged -= Source_PropertyChanged;
            }

            public void UnsubscribeAll()
            {
                UnsubscribeEvents();
            }

            private void Source_EventA(object sender, EventArgs e)
            {
                _eventTracer("EventA");
            }

            private void Source_EventB(object sender, MyCancelEventArgs e)
            {
                _eventTracer("EventB " + e.Cancel);
            }

            private void Source_PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                _eventTracer("PropertyChanged: " + e.PropertyName);
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
        using WeakEventHandler;

        /// <summary>
        /// This interface will be implemented by the weaved type after weaving.
        /// </summary>
        internal interface IWeakEventTarget
        {
            /// <summary>
            /// Unsubscribes all subscribed weak events from the target.
            /// </summary>
            void Unsubscribe();
        }

        public class EventTarget<T> : IEventTarget, IWeakEventTarget
        {
            private readonly EventSource _source;
            private readonly Action<string> _eventTracer;

            private static void Source_EventA_Add(EventSource source, EventHandler<EventArgs> handler)
            {
                source.EventA += handler;
            }
            private static void Source_EventA_Remove(EventSource source, EventHandler<EventArgs> handler)
            {
                source.EventA -= handler;
            }
            private static void Source_EventB_Add(EventSource source, EventHandler<MyCancelEventArgs> handler)
            {
                source.EventB += handler;
            }
            private static void Source_EventB_Remove(EventSource source, EventHandler<MyCancelEventArgs> handler)
            {
                source.EventB -= handler;
            }
            private static void Source_EventC_Add(EventSource source, EventHandler<EventArgs> handler)
            {
                source.EventC += handler;
            }
            private static void Source_EventC_Remove(EventSource source, EventHandler<EventArgs> handler)
            {
                source.EventC -= handler;
            }
            private static void Source_PropertyChanged_Add(EventSource source, PropertyChangedEventHandler handler)
            {
                source.PropertyChanged += handler;
            }
            private static void Source_PropertyChanged_Remove(EventSource source, PropertyChangedEventHandler handler)
            {
                source.PropertyChanged -= handler;
            }

            private readonly WeakEventHandlerFodyWeakEventAdapter<EventSource, EventTarget<T>, EventArgs, EventHandler<EventArgs>> _sourceEventAAdapter;
            private readonly WeakEventHandlerFodyWeakEventAdapter<EventSource, EventTarget<T>, MyCancelEventArgs, EventHandler<MyCancelEventArgs>> _sourceEventBAdapter;
            private readonly WeakEventHandlerFodyWeakEventAdapter<EventSource, EventTarget<T>, EventArgs, EventHandler<EventArgs>> _sourceEventCAdapter;
            private readonly WeakEventHandlerFodyWeakEventAdapter<EventSource, EventTarget<T>, PropertyChangedEventArgs, PropertyChangedEventHandler> _sourcePropertyChangedAdapter;

            public EventTarget(EventSource source, Action<string> eventTracer)
            {
                _source = source;
                _eventTracer = eventTracer;

                var eventADelegate = GetStaticDelegate<EventArgs>(Source_EventA);
                var eventBDelegate = GetStaticDelegate<MyCancelEventArgs>(Source_EventB);
                var propChangeDelegate = GetStaticDelegate<PropertyChangedEventArgs>(Source_PropertyChanged);

                _sourceEventAAdapter = new WeakEventHandlerFodyWeakEventAdapter<EventSource, EventTarget<T>, EventArgs, EventHandler<EventArgs>>(this, eventADelegate, Source_EventA_Add, Source_EventA_Remove);
                _sourceEventBAdapter = new WeakEventHandlerFodyWeakEventAdapter<EventSource, EventTarget<T>, MyCancelEventArgs, EventHandler<MyCancelEventArgs>>(this, eventBDelegate, Source_EventB_Add, Source_EventB_Remove);
                _sourceEventCAdapter = new WeakEventHandlerFodyWeakEventAdapter<EventSource, EventTarget<T>, EventArgs, EventHandler<EventArgs>>(this, eventADelegate, Source_EventC_Add, Source_EventC_Remove);
                _sourcePropertyChangedAdapter = new WeakEventHandlerFodyWeakEventAdapter<EventSource, EventTarget<T>, PropertyChangedEventArgs, PropertyChangedEventHandler>(this, propChangeDelegate, Source_PropertyChanged_Add, Source_PropertyChanged_Remove);
            }

            public void SubscribeEvents()
            {
                _sourceEventAAdapter.Subscribe(_source);
                _sourceEventBAdapter.Subscribe(_source);
                _sourceEventCAdapter.Subscribe(_source);
                _sourcePropertyChangedAdapter.Subscribe(_source);
            }

            public void UnsubscribeEvents()
            {
                _sourceEventAAdapter.Unsubscribe(_source);
                _sourceEventBAdapter.Unsubscribe(_source);
                _sourceEventCAdapter.Unsubscribe(_source);
                _sourcePropertyChangedAdapter.Unsubscribe(_source);
            }

            public void Subscribe2(EventSource? source)
            {
                if (source == null)
                    return;

                _sourceEventAAdapter.Subscribe(source);
            }

            public void UnsubscribeAll()
            {
                UnsubscribeEvents();
            }

            private void Source_EventA(object sender, EventArgs e)
            {
                _eventTracer("EventA");
            }

            private void Source_EventB(object sender, MyCancelEventArgs e)
            {
                _eventTracer("EventB " + e.Cancel);
            }

            private void Source_PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                _eventTracer("PropertyChanged: " + e.PropertyName);
            }

            private Action<EventTarget<T>, object, T1> GetStaticDelegate<T1>(Action<object, T1> instanceDelegate)
                where T1 : EventArgs
            {
#if NETSTANDARD1_0 || NET40
                throw new NotImplementedException(instanceDelegate.ToString());
#else
                return (Action<EventTarget<T>, object, T1>)Delegate.CreateDelegate(typeof(Action<EventTarget<T>, object, T1>), null, instanceDelegate.Method);
#endif
            }

            void IWeakEventTarget.Unsubscribe()
            {
                _sourceEventAAdapter.Release();
                _sourceEventBAdapter.Release();
                _sourceEventCAdapter.Release();
                _sourcePropertyChangedAdapter.Release();
            }

            ~EventTarget()
            {
                ((IWeakEventTarget)this).Unsubscribe();
            }
        }
    }

    namespace Fody
    {
        using WeakEventHandler;

        public class EventTarget<T> : IEventTarget
        {
            private readonly EventSource _source;
            private readonly Action<string> _eventTracer;

            public EventTarget(EventSource source, Action<string> eventTracer)
            {
                _source = source;
                _eventTracer = eventTracer;
            }

            public void SubscribeEvents()
            {
                GetSource(GetTarget(this)).EventA += Source_EventA;

                _source.EventB += Source_EventB;
                _source.EventC += Source_EventA;
                _source.PropertyChanged += Source_PropertyChanged;
            }

            public void UnsubscribeEvents()
            {
                GetSource(GetTarget(this)).EventA -= Source_EventA;

                _source.EventB -= Source_EventB;
                _source.EventC -= Source_EventA;
                _source.PropertyChanged -= Source_PropertyChanged;
            }

            public void UnsubscribeAll()
            {
                WeakEvents.Unsubscribe(this);
            }

            public void Subscribe2(EventSource? source)
            {
                if (source == null)
                    return;

                source.EventA += Source_EventA;
            }

            [MakeWeak]
            private void Source_EventA(object sender, EventArgs e)
            {
                _eventTracer("EventA");
            }

            [MakeWeak]
            private void Source_EventB(object sender, MyCancelEventArgs e)
            {
                _eventTracer("EventB " + e.Cancel);
            }

            [MakeWeak]
            private void Source_PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                _eventTracer("PropertyChanged: " + e.PropertyName);
            }

            private static EventSource GetSource(EventTarget<T> target)
            {
                return target._source;
            }

            private static EventTarget<T> GetTarget(EventTarget<T> target)
            {
                return target;
            }
        }
    }
}
