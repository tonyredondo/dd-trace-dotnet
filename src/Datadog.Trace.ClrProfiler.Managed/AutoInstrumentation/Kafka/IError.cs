namespace Datadog.Trace.ClrProfiler.AutoInstrumentation.Kafka
{
    /// <summary>
    /// Error interface for duck-typing
    /// </summary>
    public interface IError
    {
        /// <summary>
        /// Gets the string representation of the error
        /// </summary>
        /// <returns>The string representation of the error</returns>
        public string ToString();
    }
}
