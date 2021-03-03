using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Datadog.Trace;
using Microsoft.Extensions.Hosting;

namespace KafkaConsumer.Handlers
{
    public class KafkaConsumerHandler : IHostedService
    {
        private readonly string topic = "simpletalk_topic";
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var conf = new ConsumerConfig
            {
                GroupId = "st_consumer_group",
                BootstrapServers = "localhost:9092",
                AutoOffsetReset = AutoOffsetReset.Earliest
            };
            using (var builder = new ConsumerBuilder<Ignore,
                string>(conf).Build())
            {
                builder.Subscribe(topic);
                var cancelToken = new CancellationTokenSource();
                try
                {
                    while (true)
                    {
                        // Receive message
                        var consumer = builder.Consume(cancelToken.Token);

                        // Do original message processing
                        Console.WriteLine($"Message: {consumer.Message.Value} received from {consumer.TopicPartitionOffset}");

                        // Let's add a Span to capture this event
                        // Read the basic property headers and extract the Datadog properties
                        var headers = consumer.Message?.Headers;
                        ulong? parentSpanId = null;
                        ulong? traceId = null;
                        SamplingPriority? samplingPriority = null;

                        if (headers != null)
                        {
                            // Parse parentId header
                            if (headers.TryGetLastBytes(HttpHeaderNames.ParentId, out byte[] parentSpanIdBytes))
                            {
                                if (ulong.TryParse(Encoding.UTF8.GetString(parentSpanIdBytes), out ulong result))
                                {
                                    parentSpanId = result;
                                }
                            }

                            // Parse traceId header
                            if (headers.TryGetLastBytes(HttpHeaderNames.TraceId, out byte[] traceIdBytes))
                            {
                                if (ulong.TryParse(Encoding.UTF8.GetString(traceIdBytes), out ulong result))
                                {
                                    traceId = result;
                                }
                            }

                            // Parse samplingPriority header
                            if (headers.TryGetLastBytes(HttpHeaderNames.SamplingPriority, out byte[] samplingPriorityBytes))
                            {
                                var samplingPriorityString = Encoding.UTF8.GetString(samplingPriorityBytes);
                                if (Enum.TryParse<SamplingPriority>(samplingPriorityString, out var result))
                                {
                                    samplingPriority = result;
                                }
                            }
                        }

                        // Create a new SpanContext to represent the distributed tracing information
                        SpanContext propagatedContext = null;
                        if (parentSpanId.HasValue && traceId.HasValue)
                        {
                            propagatedContext = new SpanContext(traceId, parentSpanId.Value, samplingPriority);
                        }

                        // Create the span that is a distributed trace
                        using (Scope scope = Tracer.Instance.StartActive("kafka.message", propagatedContext))
                        {
                            // Set Datadog tags
                            Span span = scope.Span;
                            span.ResourceName = $"consume {topic}";
                            span.Type = SpanTypes.Queue;
                            span.SetTag(Tags.SpanKind, SpanKinds.Consumer);
                            span.SetTag("kafka.topic", consumer.Topic);
                            span.SetTag("message.length", consumer.Message.Value?.Length.ToString() ?? "0");

                            // Do work inside the Datadog trace
                            Thread.Sleep(500);
                        }
                    }
                }
                catch (Exception)
                {
                    builder.Close();
                }
            }
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
