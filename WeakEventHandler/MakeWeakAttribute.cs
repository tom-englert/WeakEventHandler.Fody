namespace WeakEventHandler
{
    using System;

    /// <summary>
    /// Add this attribute to any event handler with the signature "void Method(object, EventArgs)" to weave in the WeakEventAdapter between the event source and the event handler.
    /// This will allow the event handlers object to be garbage collected even when the event is still subscribed.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class MakeWeakAttribute : Attribute
    {
    }
}
