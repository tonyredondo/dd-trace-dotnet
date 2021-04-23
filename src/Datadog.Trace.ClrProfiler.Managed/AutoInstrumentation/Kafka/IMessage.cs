namespace Datadog.Trace.ClrProfiler.AutoInstrumentation.Kafka
{
    /// <summary>
    /// Message interface for duck-typing
    /// </summary>
    public interface IMessage
    {
        /// <summary>
        /// Gets the value of the message
        /// </summary>
        public object Value { get; }
    }
}
