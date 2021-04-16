using System;
using Datadog.Trace.ClrProfiler.CallTarget;
using Datadog.Trace.Logging;
using Datadog.Trace.Tagging;

namespace Datadog.Trace.ClrProfiler.AutoInstrumentation.Kafka
{
    /// <summary>
    /// Confluent.Kafka Producer.TypedDeliveryHandlerShim_Action.HandleDeliveryReport calltarget instrumentation
    /// </summary>
    [InstrumentMethod(
        AssemblyName = "Confluent.Kafka",
        TypeName = "Confluent.Kafka.Producer`2+TypedDeliveryHandlerShim_Action",
        MethodName = ".ctor",
        ReturnTypeName = ClrNames.Void,
        ParameterTypeNames = new[] { ClrNames.String, "!0", "!1", KafkaConstants.ActionOfDeliveryReportTypeName },
        MinimumVersion = "1.4.0",
        MaximumVersion = "1.*.*",
        IntegrationName = KafkaConstants.IntegrationName)]
    public class KafkaProduceSyncDeliveryHandlerIntegration
    {
        private static readonly IDatadogLogger Logger = DatadogLogging.GetLoggerFor<KafkaProduceSyncDeliveryHandlerIntegration>();

        /// <summary>
        /// OnMethodBegin callback
        /// </summary>
        /// <typeparam name="TTarget">Type of the target</typeparam>
        /// <typeparam name="TKey">Type of the message key</typeparam>
        /// <typeparam name="TValue">Type of the message value</typeparam>
        /// <typeparam name="TDeliveryReport">Type of the delivery report</typeparam>
        /// <param name="instance">Instance value, aka `this` of the instrumented method.</param>
        /// <param name="topic">The topic to which the message was sent</param>
        /// <param name="key">The message key value</param>
        /// <param name="value">The message value</param>
        /// <param name="handler">The delivery handler instance</param>
        /// <returns>Calltarget state value</returns>
        public static CallTargetState OnMethodBegin<TTarget, TKey, TValue, TDeliveryReport>(TTarget instance, string topic, TKey key, TValue value, Action<TDeliveryReport> handler)
            where TDeliveryReport : IDeliveryReport
        {
            var fieldInfo = typeof(TTarget).GetField("Handler");
            if (fieldInfo is null)
            {
                Logger.Error("Error retrieving field info for Handler for type {TTarget}", typeof(TTarget));
                return CallTargetState.GetDefault();
            }

            Span span = KafkaHelper.CreateProduceSpan(Tracer.Instance, topic);

            Action<TDeliveryReport> newHandler = report =>
            {
                if (span.Tags is KafkaTags tags)
                {
                    if (report?.Error is not null)
                    {
                        // Set the error tags manually, as we don't have an exception + stack trace here
                        // Should we create one?
                        var ex = new Exception(report.Error.ToString());
                        span.SetException(ex);
                    }

                    if (report?.Partition is not null)
                    {
                        tags.Partition = report.Partition.ToString();
                    }

                    if (report?.Offset is not null)
                    {
                        tags.Offset = report.Offset.ToString();
                    }
                }

                try
                {
                    handler?.Invoke(report);
                }
                finally
                {
                    span.Finish();
                }
            };

            Action updateHandlerAction = () => fieldInfo.SetValue(instance, newHandler);

            // store the handler as a state value so we can wrap it later
            return new CallTargetState(scope: null, state: updateHandlerAction);
        }

        /// <summary>
        /// OnMethodEnd callback
        /// </summary>
        /// <typeparam name="TTarget">Type of the target</typeparam>
        /// <param name="instance">Instance value, aka `this` of the instrumented method.</param>
        /// <param name="exception">Exception instance in case the original code threw an exception.</param>
        /// <param name="state">Calltarget state value</param>
        /// <returns>A response value, in an async scenario will be T of Task of T</returns>
        public static CallTargetReturn OnMethodEnd<TTarget>(TTarget instance, Exception exception, CallTargetState state)
            where TTarget : ITypedDeliveryHandlerShimAction
        {
            var updateHandlerAction = (Action)state.State;
            updateHandlerAction?.Invoke();

            return CallTargetReturn.GetDefault();
        }
    }
}
