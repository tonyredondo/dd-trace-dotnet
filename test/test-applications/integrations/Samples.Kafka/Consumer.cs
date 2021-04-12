﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Newtonsoft.Json;

namespace Samples.Kafka
{
    internal class Consumer: IDisposable
    {
        private readonly string _consumerName;
        private readonly IConsumer<Ignore, string> _consumer;

        public static int TotalAsyncMessages = 0;
        public static int TotalSyncMessages = 0;

        private Consumer(ConsumerConfig config, string topic, string consumerName)
        {
            _consumerName = consumerName;
            _consumer = new ConsumerBuilder<Ignore, string>(config).Build();
            _consumer.Subscribe(topic);
        }

        public void Consume(CancellationToken cancellationToken = default)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // will block until a message is available
                    var consumeResult = _consumer.Consume(cancellationToken);

                    if (consumeResult.IsPartitionEOF)
                    {
                        Console.WriteLine($"{_consumerName}: Reached EOF");
                    }
                    else
                    {
                        HandleMessage(consumeResult);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"{_consumerName}: Cancellation requested, exiting.");
            }
        }

        public void ConsumeWithExplicitCommit(int commitEveryXMessages, CancellationToken cancellationToken = default)
        {
            ConsumeResult<Ignore, string> consumeResult = null;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // will block until a message is available
                    consumeResult = _consumer.Consume(cancellationToken);

                    if (consumeResult.IsPartitionEOF)
                    {
                        Console.WriteLine($"{_consumerName}: Reached EOF");
                    }
                    else
                    {
                        HandleMessage(consumeResult);
                    }

                    if (consumeResult.Offset % commitEveryXMessages == 0)
                    {
                        try
                        {
                            Console.WriteLine($"{_consumerName}: committing...");
                            _consumer.Commit(consumeResult);
                        }
                        catch (KafkaException e)
                        {
                            Console.WriteLine($"{_consumerName}: commit error: {e.Error.Reason}");
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"{_consumerName}: Cancellation requested, exiting.");
            }

            // As we're doing manual commit, make sure we force a commit now
            if (consumeResult is not null)
            {
                Console.WriteLine($"{_consumerName}: committing...");
                _consumer.Commit(consumeResult);
            }
        }

        private void HandleMessage(ConsumeResult<Ignore, string> consumeResult)
        {
            var kafkaMessage = consumeResult.Message;
            Console.WriteLine($"{_consumerName}: Consuming {consumeResult.TopicPartitionOffset}: {kafkaMessage.Key}, {kafkaMessage.Value}");

            var sampleMessage = JsonConvert.DeserializeObject<SampleMessage>(kafkaMessage.Value);
            if (sampleMessage.IsProducedAsync)
            {
                Interlocked.Increment(ref TotalAsyncMessages);
            }
            else
            {
                Interlocked.Increment(ref TotalSyncMessages);

            }
        }

        public void Dispose()
        {
            Console.WriteLine($"{_consumerName}: Closing consumer");
            _consumer?.Close();
            _consumer?.Dispose();
        }

        public static Consumer Create(bool enableAutoCommit, string topic, string consumerName)
        {
            Console.WriteLine($"Creating consumer '{consumerName}' and subscribing to topic {topic}");

            var config = new ConsumerConfig
            {
                BootstrapServers = Config.KafkaBrokerHost,
                GroupId = "Samples.Kafka.TestConsumer",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = enableAutoCommit,
            };
            return new Consumer(config, topic, consumerName);
        }
    }
}
