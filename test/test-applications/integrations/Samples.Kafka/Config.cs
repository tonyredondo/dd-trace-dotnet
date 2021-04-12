using System.Collections.Generic;
using Confluent.Kafka;

namespace Samples.Kafka
{
    internal static class Config
    {
        public const string KafkaBrokerHost = "localhost:9092";

        public static ClientConfig Create()
        {
            return new ClientConfig
            {
                BootstrapServers = KafkaBrokerHost
            };
        }

        public static string GetTopic(string topicBase)
        {
#if NET452
            return $"{topicBase}-net452";
#elif NET461
            return $"{topicBase}-net461";
#elif NETCOREAPP2_1
            return $"{topicBase}-netcoreapp2_1";
#elif NETCOREAPP3_0
            return $"{topicBase}-netcoreapp3_0";
#elif NETCOREAPP3_1
            return $"{topicBase}-netcoreapp3_1";
#elif NET5_0
            return $"{topicBase}-net5_0";
#endif
        }
    }
}
