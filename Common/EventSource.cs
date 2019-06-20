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

        public void RaiseEventA1()
        {
            EventA?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseEventA2(EventArgs e)
        {
            EventA?.Invoke(this, e);
        }

        public void RaiseEventB(bool value)
        {
            EventB?.Invoke(this, new MyCancelEventArgs(value));
        }

        public void RaiseEventC()
        {
            EventC?.Invoke(this, EventArgs.Empty);
        }

        public void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
