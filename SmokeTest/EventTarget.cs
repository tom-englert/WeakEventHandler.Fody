// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
namespace SmokeTest
{
    using System;

    using Common;

    public class EventTarget
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

        [WeakEventHandler.MakeWeak]
        private void Source_UnusedEventHandler(object sender, MyCancelEventArgs e)
        {
            _eventTracer("EventB " + e.Cancel);
        }

        private static EventSource GetSource(EventTarget target)
        {
            return target._source;
        }

        private static EventTarget GetTarget(EventTarget target)
        {
            return target;
        }
    }
}
