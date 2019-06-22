// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedVariable
namespace Tests
{
    using System;
    using System.Diagnostics;

    using Common;

    using JetBrains.Annotations;

    using Template;

    using WeakEventHandler;

    using Xunit;
    using Xunit.Abstractions;

    public class ReferenceImplementationTest
    {
        [NotNull]
        private readonly ITestOutputHelper _output;

        public ReferenceImplementationTest([NotNull] ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(TargetKind.Original)]
        [InlineData(TargetKind.Weak)]
        [InlineData(TargetKind.Fody)]
        public void StandardBehavior(TargetKind targetKind)
        {
            var lastEvent = (string)null;

            var source = new EventSource();

            var target = CreateTarget(targetKind, source, e => lastEvent = e);

            source.RaiseEventA1();

            Assert.Null(lastEvent);

            target.SubscribeEvents();

            source.RaiseEventA1();

            Assert.Equal("EventA", lastEvent);

            source.RaisePropertyChanged("Test");

            Assert.Equal("PropertyChanged: Test", lastEvent);

            lastEvent = null;

            target.UnsubscribeEvents();

            source.RaiseEventA1();

            Assert.Null(lastEvent);
        }

        [Theory]
        [InlineData(TargetKind.Original)]
        [InlineData(TargetKind.Weak)]
        [InlineData(TargetKind.Fody)]
        public void BehaviorWhenTargetIsOutOfScope(TargetKind targetKind)
        {
            var lastEvent = (string)null;

            var source = new EventSource();

            void Inner()
            {
                var target = CreateTarget(targetKind, source, e => lastEvent = e);

                Assert.False(source.RaiseEventA1());

                Assert.Null(lastEvent);

                target.SubscribeEvents();

                Assert.True(source.RaiseEventA1());

                Assert.Equal("EventA", lastEvent);

                Assert.True(source.RaisePropertyChanged("Test"));

                Assert.Equal("PropertyChanged: Test", lastEvent);

                lastEvent = null;
            }

            Inner();

            GCCollect();

            var expected = source.RaiseEventA1();
            var isWeak = IsWeak(targetKind);
            Assert.Equal(!isWeak, expected);

            var expectedEvent = isWeak ? null : "EventA";
            Assert.Equal(expectedEvent, lastEvent);
        }

        [Theory]
        [InlineData(TargetKind.Original)]
        [InlineData(TargetKind.Weak)]
        [InlineData(TargetKind.Fody)]
        public void BehaviorWithCustomEventArgs(TargetKind targetKind)
        {
            var lastEvent = (string)null;

            var source = new EventSource();

            void Inner()
            {
                var target = CreateTarget(targetKind, source, e => lastEvent = e);

                source.RaiseEventA1();

                Assert.Null(lastEvent);

                target.SubscribeEvents();

                source.RaiseEventB(true);

                Assert.Equal("EventB True", lastEvent);

                source.RaiseEventB(false);

                Assert.Equal("EventB False", lastEvent);

                source.RaisePropertyChanged("Test");

                Assert.Equal("PropertyChanged: Test", lastEvent);

                lastEvent = null;
            }

            Inner();

            GCCollect();

            source.RaiseEventA1();

            var expected = IsWeak(targetKind) ? null : "EventA";

            Assert.Equal(expected, lastEvent);

            source.RaiseEventB(true);

            expected = IsWeak(targetKind) ? null : "EventB True";

            Assert.Equal(expected, lastEvent);

            source.RaisePropertyChanged("Test3");

            expected = IsWeak(targetKind) ? null : "PropertyChanged: Test3";

            Assert.Equal(expected, lastEvent);
        }

        [Theory]
        [InlineData(TargetKind.Fody)]
        public void UnsubscribeWeakEvents(TargetKind targetKind)
        {
            var lastEvent = (string)null;

            var source = new EventSource();

            var target = CreateTarget(targetKind, source, e => lastEvent = e);

            source.RaiseEventA1();

            Assert.Null(lastEvent);

            target.SubscribeEvents();

            source.RaiseEventA1();

            Assert.Equal("EventA", lastEvent);

            source.RaisePropertyChanged("Test");

            Assert.Equal("PropertyChanged: Test", lastEvent);

            lastEvent = null;

            WeakEvents.Unsubscribe(target);

            source.RaiseEventA1();

            Assert.Null(lastEvent);

            source.RaisePropertyChanged("Test2");

            Assert.Null(lastEvent);
        }

        [Theory]
        [InlineData(TargetKind.Original)]
        [InlineData(TargetKind.Weak)]
        [InlineData(TargetKind.Fody)]
        public void PerformanceOfSubscribeUnsubscribe(TargetKind targetKind)
        {
            const int numberOfLoops = 1000000;

            var stopwatch = new Stopwatch();

            stopwatch.Restart();

            var source = new EventSource();
            var target = CreateTarget(targetKind, source, e => { });
            for (var i = 0; i < numberOfLoops; i++)
            {
                target.SubscribeEvents();
                target.UnsubscribeEvents();
            }

            _output.WriteLine(targetKind + ": " + stopwatch.Elapsed);
        }

        [Theory]
        [InlineData(TargetKind.Original)]
        [InlineData(TargetKind.Weak)]
        [InlineData(TargetKind.Fody)]
        public void PerformanceOfRaisingEvents(TargetKind targetKind)
        {
            const int numberOfLoops = 10000000;

            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            var source = new EventSource();
            var target = CreateTarget(targetKind, source, e => { });
            target.SubscribeEvents();

            for (var i = 0; i < numberOfLoops; i++)
            {
                source.RaiseEventA1();
            }

            target.UnsubscribeEvents();

            _output.WriteLine(targetKind + ": " + stopwatch.Elapsed);
        }

        private static void GCCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.WaitForFullGCApproach();
        }

        private IEventTarget CreateTarget<T>(TargetKind targetKind, [NotNull] T source, [NotNull] Action<string> eventTracer)
            where T : EventSource
        {
            switch (targetKind)
            {
                case TargetKind.Original:
                    return new Template.Original.EventTarget<int>(source, eventTracer);
                case TargetKind.Weak:
                    return new Template.Weak.EventTarget<int>(source, eventTracer);
                case TargetKind.Fody:
                    return new Template.Fody.EventTarget<int>(source, eventTracer);
            }

            throw new InvalidOperationException();
        }

        private bool IsWeak(TargetKind targetKind)
        {
            return targetKind != TargetKind.Original;
        }

        class A
        {
            public void Add(EventHandler<EventArgs> item)
            {

            }
        }

        private void Sample()
        {
            var a = new A();
            var x = new Action<EventHandler<EventArgs>>(a.Add);
        }
    }
}
