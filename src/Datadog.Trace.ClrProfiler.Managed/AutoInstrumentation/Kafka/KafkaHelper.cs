﻿using System;
using Datadog.Trace.Logging;
using Datadog.Trace.Tagging;

namespace Datadog.Trace.ClrProfiler.AutoInstrumentation.Kafka
{
    internal static class KafkaHelper
    {
        private static readonly IDatadogLogger Log = DatadogLogging.GetLoggerFor(typeof(KafkaHelper));

        internal static Scope CreateProduceScope(Tracer tracer, ITopicPartition topicPartition, bool isTombstone)
        {
            Scope scope = null;

            try
            {
                var span = CreateProduceSpan(tracer, topicPartition?.Topic, topicPartition?.Partition, isTombstone);
                if (span is not null)
                {
                    scope = tracer.ActivateSpan(span);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating or populating scope.");
            }

            return scope;
        }

        internal static Span CreateProduceSpan(Tracer tracer, string topic, Partition? partition, bool isTombstone)
        {
            if (!Tracer.Instance.Settings.IsIntegrationEnabled(KafkaConstants.IntegrationId))
            {
                // integration disabled, don't create a scope/span, skip this trace
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
            Span span = null;

            try
            {
                var tags = new KafkaTags(SpanKinds.Producer);

                span = tracer.StartSpan(KafkaConstants.ProduceOperationName, tags, serviceName: serviceName);

                string resourceName = $"Produce Topic {(string.IsNullOrEmpty(topic) ? "kafka" : topic)}";

                span.Type = SpanTypes.Queue;
                span.ResourceName = resourceName;
                if (partition.HasValue && partition.Value.IsSpecial)
                {
                    tags.Partition = partition.ToString();
                }

                if (isTombstone)
                {
                    tags.Tombstone = "true";
                }

                // Producer spans should always be measured
                span.SetTag(Tags.Measured, "1");

                tags.SetAnalyticsSampleRate(KafkaConstants.IntegrationId, tracer.Settings, enabledWithGlobalSetting: false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating or populating span.");
            }

            return span;
        }
    }
}
