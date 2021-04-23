using System;
using System.Reflection;
using Datadog.Trace.ClrProfiler.CallTarget;
using Datadog.Trace.DuckTyping;
using Datadog.Trace.Logging;
using Datadog.Trace.Tagging;

namespace Datadog.Trace.ClrProfiler.AutoInstrumentation.Kafka
{
    /// <summary>
    /// Confluent.Kafka Producer.TypedDeliveryHandlerShim_Action.HandleDeliveryReport calltarget instrumentation
    /// </summary>
    [InstrumentMethod(
        AssemblyName = "Confluent.Kafka",
        TypeName = "Confluent.Kafka.Producer`2+TypedDeliveryHandlerShim_Action",
        MethodName = ".ctor",
        ReturnTypeName = ClrNames.Void,
        ParameterTypeNames = new[] { ClrNames.String, "!0", "!1", KafkaConstants.ActionOfDeliveryReportTypeName },
        MinimumVersion = "1.4.0",
        MaximumVersion = "1.*.*",
        IntegrationName = KafkaConstants.IntegrationName)]
    public class KafkaProduceSyncDeliveryHandlerIntegration
    {
        private static readonly IDatadogLogger Logger = DatadogLogging.GetLoggerFor<KafkaProduceSyncDeliveryHandlerIntegration>();

        /// <summary>
        /// OnMethodBegin callback
        /// </summary>
        /// <typeparam name="TTarget">Type of the target</typeparam>
        /// <typeparam name="TKey">Type of the message key</typeparam>
        /// <typeparam name="TValue">Type of the message value</typeparam>
        /// <typeparam name="TActionOfDeliveryReport">Type of the delivery report</typeparam>
        /// <param name="instance">Instance value, aka `this` of the instrumented method.</param>
        /// <param name="topic">The topic to which the message was sent</param>
        /// <param name="key">The message key value</param>
        /// <param name="value">The message value</param>
        /// <param name="handler">The delivery handler instance</param>
        /// <returns>Calltarget state value</returns>
        public static CallTargetState OnMethodBegin<TTarget, TKey, TValue, TActionOfDeliveryReport>(TTarget instance, string topic, TKey key, TValue value, TActionOfDeliveryReport handler)
        {
            Logger.Information("##### TypedDeliveryHandlerShim_Action.OnMethodBegin");
            Logger.Information("##### type is {type}, {runtimeType}", typeof(TTarget), instance.GetType());

            if (handler is null)
            {
                Logger.Information("##### Handler was null, bailing out");
                return CallTargetState.GetDefault();
            }

            // var fieldInfo = typeof(TTarget).GetField("Handler", BindingFlags.Instance | BindingFlags.Public);
            // if (fieldInfo is null)
            // {
            //     Logger.Warning("##### field info  was null, bailing out");
            //     var allFields = typeof(TTarget).GetFields();
            //     var allMembers = typeof(TTarget).GetMembers(BindingFlags.Public | BindingFlags.NonPublic);
            //     Logger.Warning("Found the following members on type {type}: {members}", typeof(TTarget), string.Join(", ", allMembers.Select(x => x.Name)));
            //     return CallTargetState.GetDefault();
            // }

            try
            {
                Span span = KafkaHelper.CreateProduceSpan(Tracer.Instance, topic, partition: null);
                var newAction = CachedWrapperDelegate<TActionOfDeliveryReport>.CreateWrapper(handler, span);

                Action<ITypedDeliveryHandlerShimAction> updateHandlerAction = inst =>
                {
                    try
                    {
                        Logger.Information("##### Setting updated handler");
                        inst.Handler = newAction;
                        // fieldInfo.SetValue(instance, newAction);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "##### Error Setting updated handler");
                        throw;
                    }
                };

                // store the call to update the handler variable as state
                // so we update it at the end of the constructor
                return new CallTargetState(scope: null, state: updateHandlerAction);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "##### TypedDeliveryHandlerShim_Action.OnMethodEnd");
                return CallTargetState.GetDefault();
            }
        }

        /// <summary>
        /// OnMethodEnd callback
        /// </summary>
        /// <typeparam name="TTarget">Type of the target</typeparam>
        /// <param name="instance">Instance value, aka `this` of the instrumented method.</param>
        /// <param name="exception">Exception instance in case the original code threw an exception.</param>
        /// <param name="state">Calltarget state value</param>
        /// <returns>A response value, in an async scenario will be T of Task of T</returns>
        public static CallTargetReturn OnMethodEnd<TTarget>(TTarget instance, Exception exception, CallTargetState state)
            // where TTarget : ITypedDeliveryHandlerShimAction
        {
            Logger.Information("##### TypedDeliveryHandlerShim_Action.OnMethodEnd");
            Logger.Information("##### type is {type}, {runtimeType}", typeof(TTarget), instance.GetType());

            if (state.State is Action<ITypedDeliveryHandlerShimAction> updateHandlerAction
             && instance.TryDuckCast<ITypedDeliveryHandlerShimAction>(out var shim))
            {
                Logger.Information("##### Handler was not null. Invoking");
                updateHandlerAction.Invoke(shim);
            }

            Logger.Information("##### Returning default");
            return CallTargetReturn.GetDefault();
        }

        /// <summary>
        /// Helper method used by <see cref="CachedWrapperDelegate{TActionDelegate}"/> to create a delegate
        /// </summary>
        /// <param name="originalHandler">The original delivery report handler </param>
        /// <param name="span">A <see cref="Span"/> that can be manipulated when the action is invoked</param>
        /// <typeparam name="TDeliveryReport">Type of the delivery report</typeparam>
        /// <returns>The wrapped action</returns>
        public static Action<TDeliveryReport> WrapAction<TDeliveryReport>(Action<TDeliveryReport> originalHandler, Span span)
        {
            return new Action<TDeliveryReport>(
                value =>
                {
                    Logger.Information("##### Delivery report handler invoked");
                    if (span.Tags is KafkaTags tags && value.TryDuckCast<IDeliveryReport>(out var report))
                    {
                        Logger.Information("##### We're in business");
                        var isError = report?.Error is not null && report.Error.IsError;
                        if (isError)
                        {
                            Logger.Information("##### Supposedly have an error {err}", report.Error);
                            // Set the error tags manually, as we don't have an exception + stack trace here
                            // Should we create one?
                            var ex = new Exception(report.Error.ToString());
                            span.SetException(ex);
                        }

                        if (report?.Partition is not null)
                        {
                            tags.Partition = report.Partition.ToString();
                        }

                        // Won't have offset if is error
                        if (!isError && report?.Offset is not null)
                        {
                            tags.Offset = report.Offset.ToString();
                        }
                    }

                    // call previous delegate
                    try
                    {
                        Logger.Information("##### Invoking original handler");
                        originalHandler(value);
                    }
                    finally
                    {
                        Logger.Information("##### Closing span");
                        span.Finish();
                    }
                });
        }

        /// <summary>
        /// Helper class for creating a <typeparamref name="TActionDelegate"/> that wraps an <see cref="Action{T}"/>,
        /// </summary>
        /// <typeparam name="TActionDelegate">Makes the assumption that TActionDelegate is an <see cref="Action{T}"/></typeparam>
        internal static class CachedWrapperDelegate<TActionDelegate>
        {
            private static readonly CreateWrapperDelegate _createWrapper;

            static CachedWrapperDelegate()
            {
                // This type makes the following assumption: TActionDelegate = Action<TParam> !

                // Get the Action<T> WrapHelper.WrapAction<T>(Action<T> value) methodinfo
                var wrapActionMethod = typeof(KafkaProduceSyncDeliveryHandlerIntegration)
                   .GetMethod(nameof(WrapAction), BindingFlags.Public | BindingFlags.Static);

                // Create the generic method using the inner generic types of TActionDelegate => TParam
                wrapActionMethod = wrapActionMethod.MakeGenericMethod(typeof(TActionDelegate).GetGenericArguments());

                // With Action<TParam> WrapHelper.WrapAction<TParam>(Action<TParam> value) method info we create a delegate
                _createWrapper = (CreateWrapperDelegate)wrapActionMethod.CreateDelegate(typeof(CreateWrapperDelegate));
            }

            private delegate TActionDelegate CreateWrapperDelegate(TActionDelegate value, Span span);

            public static TActionDelegate CreateWrapper(TActionDelegate value, Span span)
            {
                // we call the delegate passing the instance of the previous delegate
                return _createWrapper(value, span);
            }
        }
    }
}
