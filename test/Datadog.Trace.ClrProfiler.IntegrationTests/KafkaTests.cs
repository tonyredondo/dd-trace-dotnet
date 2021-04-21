using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Datadog.Core.Tools;
using Datadog.Trace.TestHelpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Datadog.Trace.ClrProfiler.IntegrationTests
{
    [Collection(nameof(KafkaTestsCollection))]
    public class KafkaTests : TestHelper
    {
        private const int ExpectedSuccessProducerSpans = 20; // 31 once we have delivery handler
        private const int ExpectedErroneouslySuccessfulProducerSpans = 0; // If no delivery handler
        private const int ExpectedErrorProducerSpans = 1; // 2 when we have delivery handler When no delivery handler, error won't be caught
        private const int ExpectedConsumerSpans = 0;
        private const int TotalExpectedSpanCount = ExpectedConsumerSpans
                                                 + ExpectedSuccessProducerSpans
                                                 + ExpectedErroneouslySuccessfulProducerSpans
                                                 + ExpectedErrorProducerSpans;

        private const string ErrorProducerResourceName = "Produce Topic INVALID-TOPIC";

        public KafkaTests(ITestOutputHelper output)
            : base("Kafka", output)
        {
            SetServiceVersion("1.0.0");
            SetCallTargetSettings(enableCallTarget: true, enableMethodInlining: true);
            SetEnvironmentVariable("DD_TRACE_DEBUG", "1");
        }

        [Theory]
        [MemberData(nameof(PackageVersions.Kafka), MemberType = typeof(PackageVersions))]
        [Trait("Category", "EndToEnd")]
        public void SubmitsTraces(string packageVersion)
        {
            var agentPort = TcpPortProvider.GetOpenPort();

            using var agent = new MockTracerAgent(agentPort);
            using var processResult = RunSampleAndWaitForExit(agent.Port, arguments: $"{TestPrefix}", packageVersion: packageVersion);

            processResult.ExitCode.Should().BeGreaterOrEqualTo(0);

            var allSpans = agent.WaitForSpans(TotalExpectedSpanCount, timeoutInMilliseconds: 10_000);
            allSpans.Should().HaveCount(TotalExpectedSpanCount);

            var allProducerSpans = allSpans.Where(x => x.Name == "kafka.produce").ToList();
            var successfulProducerSpans = allProducerSpans.Where(x => x.Error == 0).ToList();
            var errorProducerSpans = allProducerSpans.Where(x => x.Error > 0).ToList();

            var resourceName = GetSuccessfulResourceName();
            VerifyProducerSpanProperties(successfulProducerSpans, resourceName, ExpectedSuccessProducerSpans);
            VerifyProducerSpanProperties(errorProducerSpans, ErrorProducerResourceName, ExpectedErrorProducerSpans);

            // verify have error
            errorProducerSpans.Should()
                              .OnlyContain(x => x.Tags.ContainsKey(Tags.ErrorType))
                              .And.OnlyContain(x => x.Tags[Tags.ErrorType] == "Confluent.Kafka.ProduceException`2[System.String,System.String]");
        }

        private void VerifyProducerSpanProperties(List<MockTracerAgent.Span> producerSpans, string resourceName, int expectedCount)
        {
            producerSpans.Should()
                         .HaveCount(expectedCount)
                         .And.OnlyContain(x => x.Service == "Samples.Kafka-kafka")
                         .And.OnlyContain(x => x.Resource == resourceName);

            // // TODO: Confirm partition is displayed correctly [0], [1]
            // // https://github.com/confluentinc/confluent-kafka-dotnet/blob/master/src/Confluent.Kafka/Partition.cs#L217-L224
            // var producerTags = producerSpans.Select(x => x.Tags).ToList();
            // producerTags.Should()
            //             .OnlyContain(tags => tags.ContainsKey(Trace.Tags.Partition));

            // var producerPartitionTags = producerTags.Select(tags => tags[Tags.Partition]);
            // producerPartitionTags.Should().OnlyContain(tag => Regex.IsMatch(tag, @"^\[[0-9]\]$"));
        }

        private string GetSuccessfulResourceName()
        {
            var resourceNameBase = "Produce Topic sample-topic";
#if NET452
            return $"{resourceNameBase}-net452";
#elif NET461
            return $"{resourceNameBase}-net461";
#elif NETCOREAPP2_1
            return $"{resourceNameBase}-netcoreapp2_1";
#elif NETCOREAPP3_0
            return $"{resourceNameBase}-netcoreapp3_0";
#elif NETCOREAPP3_1
            return $"{resourceNameBase}-netcoreapp3_1";
#elif NET5_0
            return $"{resourceNameBase}-net5_0";
#endif
        }

        [CollectionDefinition(nameof(KafkaTestsCollection), DisableParallelization = true)]
        public class KafkaTestsCollection
        {
        }
    }
}
