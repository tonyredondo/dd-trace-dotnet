using System;
using Datadog.Trace.Logging;
using Datadog.Trace.Tagging;

namespace Datadog.Trace.ClrProfiler.AutoInstrumentation.Kafka
{
    internal static class KafkaHelper
    {
        private static readonly IDatadogLogger Log = DatadogLogging.GetLoggerFor(typeof(KafkaHelper));

        internal static Scope CreateProduceScope(Tracer tracer, ITopicPartition topicPartition)
        {
            if (!Tracer.Instance.Settings.IsIntegrationEnabled(KafkaConstants.IntegrationId))
            {
                // integration disabled, don't create a scope, skip this trace
                return null;
            }

            var parent = tracer.ActiveScope?.Span;
            if (parent is not null &&
                parent.OperationName == KafkaConstants.ProduceOperationName &&
                parent.GetTag(Tags.InstrumentationName) != null)
            {
                return null;
            }

            string serviceName = tracer.Settings.GetServiceName(tracer, KafkaConstants.ServiceName);
            Scope scope = null;

            try
            {
                var tags = new KafkaTags(SpanKinds.Producer);

                scope = tracer.StartActiveWithTags(KafkaConstants.ProduceOperationName, serviceName: serviceName, tags: tags);
                string topic = topicPartition?.Topic ?? "kafka";
                string resourceName = $"Produce Topic {topic}";

                var span = scope.Span;
                span.Type = SpanTypes.Queue;
                span.ResourceName = resourceName;

                // Record the partition we're trying to send it to
                // This is generally not set, but _can_ be
                // it will be updated on successful delivery
                tags.Partition = topicPartition?.Partition.ToString();

                tags.SetAnalyticsSampleRate(KafkaConstants.IntegrationId, tracer.Settings, enabledWithGlobalSetting: false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating or populating scope.");
            }

            return scope;
        }
    }
}
