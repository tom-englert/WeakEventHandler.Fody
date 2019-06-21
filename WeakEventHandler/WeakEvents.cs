// ReSharper disable UnusedMember.Global
namespace WeakEventHandler
{
    /// <summary>
    /// This interface will be implemented by the weaved type after weaving.
    /// </summary>
    internal interface IWeakEventHandlerFodyWeakEventTarget
    {
        /// <summary>
        /// Unsubscribes all subscribed weak events from the target.
        /// </summary>
        void Unsubscribe();
    }

    /// <summary>
    /// Helper methods for weak events.
    /// </summary>
    public static class WeakEvents
    {
        /// <summary>
        /// Unsubscribes all subscribed weak events from the target.
        /// </summary>
        /// <param name="target">The target.</param>
        public static void Unsubscribe(object target)
        {
            ((IWeakEventHandlerFodyWeakEventTarget)target).Unsubscribe();
        }
    }
}
