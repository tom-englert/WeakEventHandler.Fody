namespace Common
{
    using System;
    using System.ComponentModel;

    public class MyCancelEventArgs : EventArgs
    {
        public MyCancelEventArgs(bool cancel)
        {
            Cancel = cancel;
        }

        public bool Cancel { get; }
    }

    public class EventSource : INotifyPropertyChanged
    {
        public event EventHandler<EventArgs> EventA;
        public event EventHandler<MyCancelEventArgs> EventB;
        public event EventHandler<EventArgs> EventC;
        public event PropertyChangedEventHandler PropertyChanged;

        public bool RaiseEventA1()
        {
            var eventHandler = EventA;
            if (eventHandler == null)
                return false;
            eventHandler(this, EventArgs.Empty);
            return true;
        }

        public bool RaiseEventA2(EventArgs e)
        {
            var eventHandler = EventA;
            if (eventHandler == null)
                return false;
            eventHandler.Invoke(this, e);
            return true;
        }

        public bool RaiseEventB(bool value)
        {
            var eventHandler = EventB;
            if (eventHandler == null)
                return false;
            eventHandler.Invoke(this, new MyCancelEventArgs(value));
            return true;
        }

        public bool RaiseEventC()
        {
            var eventHandler = EventC;
            if (eventHandler == null)
                return false;
            eventHandler.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool RaisePropertyChanged(string propertyName)
        {
            var eventHandler = PropertyChanged;
            if (eventHandler == null)
                return false;
            eventHandler.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
