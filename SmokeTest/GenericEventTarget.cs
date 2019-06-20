// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
namespace SmokeTest
{
    using System;
    using System.ComponentModel;

    using Common;

    using JetBrains.Annotations;

    public class GenericEventTarget<T>
    {
        private readonly EventSource _source;
        private readonly Action<string> _eventTracer;

        public GenericEventTarget(EventSource source, Action<string> eventTracer, T generic)
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

        [WeakEventHandler.MakeWeak]
        private void Sender_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            _eventTracer("PropertyChanged");
        }

        private static EventSource GetSource([NotNull] GenericEventTarget<T> target)
        {
            return target._source;
        }

        private static GenericEventTarget<T> GetTarget([NotNull] GenericEventTarget<T> target)
        {
            return target;
        }

        private void Attach([CanBeNull] INotifyPropertyChanged sender)
        {
            if (sender == null)
                return;

            sender.PropertyChanged += Sender_PropertyChanged;
        }
    }
}
