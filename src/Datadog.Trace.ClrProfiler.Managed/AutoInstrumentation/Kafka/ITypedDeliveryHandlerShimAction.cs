using System;

namespace Datadog.Trace.ClrProfiler.AutoInstrumentation.Kafka
{
    /// <summary>
    /// TypedDeliveryHandlerShim_Action for duck-typing
    /// </summary>
    public interface ITypedDeliveryHandlerShimAction
    {
        /// <summary>
        /// Gets the delivery report handler
        /// </summary>
        public Action<IDeliveryReport> Handler { get; }
    }
}
