namespace SmokeTest
{
    using System;

    using Common;

    public class FailingConstructorEventTarget
    {
        private readonly EventSource _source;

        public FailingConstructorEventTarget(EventSource source)
            : this(source, Validate(source))
        {
        }

        // Throws before the weak adapter fields are initialized, but the finalizer still runs on the partially constructed object.
        private FailingConstructorEventTarget(EventSource source, object validated)
        {
            _source = source;
            _source.EventA += Source_EventA;
        }

        private static object Validate(EventSource source)
        {
            if (source.GetType() == typeof(EventSource))
                throw new ArgumentException("Invalid source", nameof(source));

            return source;
        }

        [WeakEventHandler.MakeWeak]
        private void Source_EventA(object sender, EventArgs e)
        {
        }
    }
}
