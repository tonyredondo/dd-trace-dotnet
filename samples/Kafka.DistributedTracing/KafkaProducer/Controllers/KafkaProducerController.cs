using System;
using System.Text;
using System.Threading;
using Confluent.Kafka;
using Datadog.Trace;
using Microsoft.AspNetCore.Mvc;

namespace KafkaProducer.Controllers
{
    [Route("api/kafka")]
    [ApiController]
    public class KafkaProducerController : ControllerBase
    {
        private readonly ProducerConfig config = new ProducerConfig
        { BootstrapServers = "localhost:9092" };
        private readonly string topic = "simpletalk_topic";
        [HttpPost]
        public IActionResult Post([FromQuery] string message)
        {
            return Created(string.Empty, SendToKafka(topic, message));
        }
        private Object SendToKafka(string topic, string message)
        {
            using (var producer =
                 new ProducerBuilder<Null, string>(config).Build())
            {
                try
                {
                    DeliveryResult<Null, string> deliveryResult = null;
                    using (var scope = Tracer.Instance.StartActive("kafka.message"))
                    {
                        // Define the message
                        var kafkaMessage = new Message<Null, string> { Value = message };

                        // Add properties to the Headers dictionary in the following way:
                        //  - "x-datadog-trace-id": "<trace_id>"
                        //  - "x-datadog-parent-id": "<span_id>"
                        //  - "x-datadog-sampling-priority": "<sampling_priority>"
                        if (kafkaMessage.Headers == null)
                        {
                            kafkaMessage.Headers = new Headers();
                        }

                        kafkaMessage.Headers.Add(HttpHeaderNames.ParentId, Encoding.UTF8.GetBytes(scope.Span.SpanId.ToString()));
                        kafkaMessage.Headers.Add(HttpHeaderNames.TraceId, Encoding.UTF8.GetBytes(scope.Span.TraceId.ToString()));
                        kafkaMessage.Headers.Add(HttpHeaderNames.SamplingPriority, Encoding.UTF8.GetBytes(scope.Span.GetTag(Tags.SamplingPriority)));
                        kafkaMessage.Headers.Add(HttpHeaderNames.TraceId, Encoding.UTF8.GetBytes(scope.Span.TraceId.ToString()));

                        // Do work inside the Datadog trace
                        Thread.Sleep(500);

                        // Produce message
                        deliveryResult = producer.ProduceAsync(topic, kafkaMessage)
                            .GetAwaiter()
                            .GetResult();

                        // Set Datadog tags
                        Span span = scope.Span;
                        span.ResourceName = $"produce {topic}";
                        span.Type = SpanTypes.Queue;
                        span.SetTag(Tags.SpanKind, SpanKinds.Producer);
                        span.SetTag("kafka.topic", topic);
                        span.SetTag("message.length", message?.Length.ToString() ?? "0");
                    }

                    return deliveryResult;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Oops, something went wrong: {e}");
                }
            }
            return null;
        }
    }
}
