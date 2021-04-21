using System;
using System.Threading.Tasks;
using Confluent.Kafka;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Samples.Kafka
{
    internal static class Producer
    {
        // Flush every x messages
        private const int FlushInterval = 3;
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(5);

        private static readonly string[] Keys = new[] { "apple", "banana", "orange", "strawberry", "kiwi" };

        public static async Task ProduceAsync(string topic, int numMessages, ClientConfig config)
        {
            using (var producer = new ProducerBuilder<string, string>(config).Build())
            {
                for (var i=0; i<numMessages; ++i)
                {
                    var key = Keys[i % Keys.Length];
                    var value = GetMessage(i, isProducedAsync: true);
                    var message = new Message<string, string> { Key = key, Value = value };

                    Console.WriteLine($"Producing record {i}: {key}...");

                    try
                    {
                        var deliveryResult = await producer.ProduceAsync(topic, message);
                        Console.WriteLine($"Produced message to: {deliveryResult.TopicPartitionOffset}");

                    }
                    catch (ProduceException<string, string> ex)
                    {
                        Console.WriteLine($"Failed to deliver message: {ex.Error.Reason}");
                    }
                }

                Console.WriteLine($"Finished producing {numMessages} messages to topic {topic}");
            }
        }

        public static void Produce(string topic, int numMessages, ClientConfig config, bool handleDelivery)
        {
            Produce(topic, numMessages, config, handleDelivery ? HandleDelivery : null);
        }

        private static void Produce(string topic, int numMessages, ClientConfig config, Action<DeliveryReport<string, string>> deliveryHandler)
        {
            using (var producer = new ProducerBuilder<string, string>(config).Build())
            {
                for (var i=0; i<numMessages; ++i)
                {
                    var key = Keys[i % Keys.Length];
                    var value = GetMessage(i, isProducedAsync: false);
                    var message = new Message<string, string> { Key = key, Value = value };

                    Console.WriteLine($"Producing record {i}: {key}...");

                    producer.Produce(topic, message, deliveryHandler);

                    if (numMessages % FlushInterval == 0)
                    {
                        producer.Flush(FlushTimeout);
                    }
                }
                producer.Flush(FlushTimeout);

                Console.WriteLine($"Finished producing {numMessages} messages to topic {topic}");
            }
        }

        private static void HandleDelivery(DeliveryReport<string, string> deliveryReport)
        {
            if (deliveryReport.Error.Code != ErrorCode.NoError)
            {
                Console.WriteLine($"Failed to deliver message: {deliveryReport.Error.Reason}");
            }
            else
            {
                Console.WriteLine($"Produced message to: {deliveryReport.TopicPartitionOffset}");
            }
        }

        static string GetMessage(int iteration, bool isProducedAsync)
        {
            var message = new SampleMessage("fruit", iteration, isProducedAsync);
            return JObject.FromObject(message).ToString(Formatting.None);
        }
    }

    public class SampleMessage
    {
        public string Category { get; }
        public int MessageNumber { get; }
        public bool IsProducedAsync { get; }

        public SampleMessage(string category, int messageNumber, bool isProducedAsync)
        {
            Category = category;
            MessageNumber = messageNumber;
            IsProducedAsync = isProducedAsync;
        }
    }
}
