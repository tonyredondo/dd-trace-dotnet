using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;

namespace Samples.Kafka
{
    class Program
    {

        // based on https://github.com/confluentinc/examples/blob/6.1.1-post/clients/cloud/csharp/Program.cs
        static async Task Main(string[] args)
        {
            var topic = Config.GetTopic("sample-topic");
            var config = Config.Create();

            await TopicHelpers.TryDeleteTopic(topic, config);

            await TopicHelpers.TryCreateTopic(
                topic,
                numPartitions: 3,
                replicationFactor: 1,
                config);

            await ConsumeAndProduceMessages(topic, config);

            Console.WriteLine($"Shut down complete");
        }

        private static async Task ConsumeAndProduceMessages(string topic, ClientConfig config)
        {
            var numberOfMessagesPerProducer = 10;
            var commitPeriod = 3;

            var cts = new CancellationTokenSource();

            using var consumer1 = Consumer.Create(enableAutoCommit: true, topic, consumerName: "AutoCommitConsumer1");
            using var consumer2 = Consumer.Create(enableAutoCommit: false, topic, consumerName: "ManualCommitConsumer2");

            var consumeTask1 = Task.Run(() => consumer1.Consume(cts.Token));
            var consumeTask2 = Task.Run(() => consumer2.ConsumeWithExplicitCommit(commitEveryXMessages: commitPeriod, cts.Token));

            // produce messages sync and async
            Producer.Produce(topic, numberOfMessagesPerProducer, config, handleDelivery: false);

            Producer.Produce(topic, numberOfMessagesPerProducer, config, handleDelivery: true);

            await Producer.ProduceAsync(topic, numberOfMessagesPerProducer, config);

            // try to produce invalid messages
            const string invalidTopic = "INVALID-TOPIC";
            // Producer.Produce(invalidTopic, 1, config, handleDelivery: false); // failure won't be logged, more of a pain to test
            Producer.Produce(invalidTopic, 1, config, handleDelivery: true); // failure should be logged by delivery handler

            try
            {
                await Producer.ProduceAsync(invalidTopic, 1, config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error producing a message to an unknown topic (expected): {ex}");
            }

            // Wait for all messages to be consumed
            // This assumes that the topic starts empty, and nothing else is producing to the topic
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                var syncCount = Volatile.Read(ref Consumer.TotalSyncMessages);
                var asyncCount = Volatile.Read(ref Consumer.TotalAsyncMessages);
                if (syncCount >= numberOfMessagesPerProducer && asyncCount >= numberOfMessagesPerProducer)
                {
                    Console.WriteLine($"All messages produced and consumed");
                    break;
                }

                if (DateTime.UtcNow > deadline)
                {
                    Console.WriteLine($"Exiting consumer: did not consume all messages syncCount {syncCount}, asyncCount {asyncCount}");
                    break;
                }


                await Task.Delay(1000);
            }

            cts.Cancel();
            Console.WriteLine($"Waiting for graceful exit...");

            await Task.WhenAny(
                Task.WhenAll(consumeTask1, consumeTask2),
                Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }
}
