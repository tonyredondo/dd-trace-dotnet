using System;
using Datadog.Trace.Agent;
using Datadog.Trace.ClrProfiler.AutoInstrumentation.Kafka;
using Datadog.Trace.Configuration;
using Datadog.Trace.DuckTyping;
using Datadog.Trace.Sampling;
using FluentAssertions;
using Moq;
using Xunit;

namespace Datadog.Trace.ClrProfiler.Managed.Tests
{
    public class CachedWrapperDelegateTests
    {
        [Fact]
        public void CanCreateWrapperDelegate()
        {
            var wasOriginalInvoked = false;
            var testReport = new TestDeliveryReport { Success = true };
            Action<TestDeliveryReport> original = x =>
            {
                wasOriginalInvoked = true;
                x.Should().BeSameAs(testReport);
            };

            var tracer = GetTracer();
            var span = tracer.StartSpan("Test operation");
            var wrapper = KafkaProduceSyncDeliveryHandlerIntegration.CachedWrapperDelegate<Action<TestDeliveryReport>>.CreateWrapper(original, span);

            wrapper.Invoke(testReport);
            wasOriginalInvoked.Should().BeTrue();
            span.IsFinished.Should().BeTrue();
        }

        [Fact]
        public void CanDuckTypeHandlerMethod()
        {
            var shim = Parent<string, string>.GetShim();

            var duckShim = shim.DuckCast<ITypedDeliveryHandlerShimAction>();

            duckShim.Should().NotBeNull();
        }

        private static Tracer GetTracer()
        {
            var settings = new TracerSettings();
            var writerMock = new Mock<IAgentWriter>();
            var samplerMock = new Mock<ISampler>();

            return new Tracer(settings, writerMock.Object, samplerMock.Object, scopeManager: null, statsd: null);
        }

        /// <summary>
        /// Has IDeliveryReport shape for duck typing
        /// </summary>
        public class TestDeliveryReport
        {
            public IError Error { get; set; }

            public Partition Partition { get; set; }

            public Offset Offset { get; set; }

            public bool Success { get; set; }
        }

#pragma warning disable SA1401 // Field should be private
        public class TestDeliveryReport<TKey, TValue>
        {
            public TKey Key;

            public TValue Value;
        }

        internal class Parent<TKey, TValue>
        {
            public static object GetShim() => new TestShim { Handler = x => { } };

            private class TestShim
            {
                public Action<TestDeliveryReport<TKey, TValue>> Handler;
            }
        }
#pragma warning restore SA1401 // Field should be private

    }
}
