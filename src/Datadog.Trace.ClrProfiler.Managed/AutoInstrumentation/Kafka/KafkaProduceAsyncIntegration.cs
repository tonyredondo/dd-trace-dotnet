﻿using System;
using System.Threading;
using Datadog.Trace.ClrProfiler.CallTarget;
using Datadog.Trace.DuckTyping;
using Datadog.Trace.Tagging;

namespace Datadog.Trace.ClrProfiler.AutoInstrumentation.Kafka
{
    /// <summary>
    /// Confluent.Kafka Producer.Produce calltarget instrumentation
    /// </summary>
    [InstrumentMethod(
        AssemblyName = "Confluent.Kafka",
        TypeName = "Confluent.Kafka.Producer`2",
        MethodName = "ProduceAsync",
        ReturnTypeName = KafkaConstants.TaskDeliveryReportTypeName,
        ParameterTypeNames = new[] { KafkaConstants.TopicPartitionTypeName, KafkaConstants.MessageTypeName, ClrNames.CancellationToken },
        MinimumVersion = "1.4.0",
        MaximumVersion = "1.*.*",
        IntegrationName = KafkaConstants.IntegrationName)]
    public class KafkaProduceAsyncIntegration
    {
        /// <summary>
        /// OnMethodBegin callback
        /// </summary>
        /// <typeparam name="TTarget">Type of the target</typeparam>
        /// <typeparam name="TTopicPartition">Type of the TopicPartition</typeparam>
        /// <typeparam name="TMessage">Type of the message</typeparam>
        /// <param name="instance">Instance value, aka `this` of the instrumented method.</param>
        /// <param name="topicPartition">TopicPartition instance</param>
        /// <param name="message">Message instance</param>
        /// <param name="cancellationToken">CancellationToken instance</param>
        /// <returns>Calltarget state value</returns>
        public static CallTargetState OnMethodBegin<TTarget, TTopicPartition, TMessage>(TTarget instance, TTopicPartition topicPartition, TMessage message, CancellationToken cancellationToken)
            where TTopicPartition : ITopicPartition
            where TMessage : IMessage
        {
            var isTombstone = message.Value is null;
            Scope scope = KafkaHelper.CreateProduceScope(Tracer.Instance, topicPartition, isTombstone: isTombstone);
            if (scope is not null)
            {
                return new CallTargetState(scope);
            }

            return CallTargetState.GetDefault();
        }

        /// <summary>
        /// OnAsyncMethodEnd callback
        /// </summary>
        /// <typeparam name="TTarget">Type of the target</typeparam>
        /// <typeparam name="TResponse">Type of the response, in an async scenario will be T of Task of T</typeparam>
        /// <param name="instance">Instance value, aka `this` of the instrumented method.</param>
        /// <param name="response">Response instance</param>
        /// <param name="exception">Exception instance in case the original code threw an exception.</param>
        /// <param name="state">Calltarget state value</param>
        /// <returns>A response value, in an async scenario will be T of Task of T</returns>
        public static TResponse OnAsyncMethodEnd<TTarget, TResponse>(TTarget instance, TResponse response, Exception exception, CallTargetState state)
        where TResponse : IDeliveryResult
        {
            if (state.Scope?.Span?.Tags is KafkaTags tags)
            {
                IDeliveryResult deliveryResult = null;
                if (exception is not null)
                {
                    var produceException = exception.DuckAs<IProduceException>();
                    if (produceException is not null)
                    {
                        deliveryResult = produceException.DeliveryResult;
                    }
                }
                else if (response is not null)
                {
                    deliveryResult = response;
                }

                if (deliveryResult is not null)
                {
                    tags.Partition = deliveryResult.Partition.ToString();
                    tags.Offset = deliveryResult.Offset.ToString();
                }
            }

            state.Scope?.DisposeWithException(exception);
            return response;
        }
    }
}
