using System;
using System.Collections.Generic;
using System.Text;

namespace Common
{
    public class MyCancelEventArgs : EventArgs
    {
        public MyCancelEventArgs(bool cancel)
        {
            Cancel = cancel;
        }

        public bool Cancel { get; }
    }

    public class EventSource
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

        public void OnEventC()
        {
            EventC?.Invoke(this, EventArgs.Empty);
        }
    }
}
