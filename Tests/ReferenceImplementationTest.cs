namespace Tests
{
    using System;
    using System.Diagnostics;

    using Common;

    using JetBrains.Annotations;

    using Template;

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

            source.OnEventA1();

            Assert.Null(lastEvent);

            target.Subscribe();

            source.OnEventA1();

            Assert.Equal("EventA", lastEvent);

            lastEvent = null;

            target.Unsubscribe();

            source.OnEventA1();

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

                source.OnEventA1();

                Assert.Null(lastEvent);

                target.Subscribe();

                source.OnEventA1();

                Assert.Equal("EventA", lastEvent);

                lastEvent = null;
            }

            Inner();

            GCCollect();

            source.OnEventA1();

            var expected = IsWeak(targetKind) ? null : "EventA";

            Assert.Equal(expected, lastEvent);
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

                source.OnEventA1();

                Assert.Null(lastEvent);

                target.Subscribe();

                source.OnEventB(true);

                Assert.Equal("EventB True", lastEvent);

                source.OnEventB(false);

                Assert.Equal("EventB False", lastEvent);

                lastEvent = null;
            }

            Inner();

            GCCollect();

            source.OnEventA1();

            var expected = IsWeak(targetKind) ? null : "EventA";

            Assert.Equal(expected, lastEvent);

            source.OnEventB(true);

            expected = IsWeak(targetKind) ? null : "EventB True";

            Assert.Equal(expected, lastEvent);
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
                target.Subscribe();
                target.Unsubscribe();
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
            target.Subscribe();

            for (var i = 0; i < numberOfLoops; i++)
            {
                source.OnEventA1();
            }

            target.Unsubscribe();

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
