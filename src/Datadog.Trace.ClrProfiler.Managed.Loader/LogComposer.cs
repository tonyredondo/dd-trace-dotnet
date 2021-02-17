﻿// <auto-generated />
// This .CS file is automatically generated. If you modify its contents, your changes will be overwritten.
// Modify the respective T4 templates if changes are required.

// <auto-generated />
// ----------- ----------- ----------- ----------- -----------
// The source code below is included via a T4 template.
// The template calling must specify the value of the <c>NamespacesAndMonikersOfLogsToCompose</c> meta-variable.
// ----------- ----------- ----------- ----------- -----------

using System;

namespace Datadog.Logging.Composition
{
    /// <summary>
    /// Collects data from many Log-sources and sends it to the specified Log Sink.
    /// This class has been generated using a T4 template. It covers the following logging components:
    ///   1) Logger type:               "Datadog.AutoInstrumentation.ManagedLoader.Log"
    ///      Logging component moniker: "ManagedLoader"
    ///
    /// TOTAL: 1 loggers.
    /// </summary>
internal static class LogComposer
    {
        private static bool s_isDebugLoggingEnabled = true;

        public static bool IsDebugLoggingEnabled
        {
            get
            {
                return s_isDebugLoggingEnabled;
            }

            set
            {
                s_isDebugLoggingEnabled = value;
                {
                    global::Datadog.AutoInstrumentation.ManagedLoader.Log.Configure.DebugLoggingEnabled(s_isDebugLoggingEnabled);
                }
            }
        }

        public static void RedirectLogs(ILogSink logSink)
        {
            {
                Datadog.AutoInstrumentation.ManagedLoader.Log.Configure.DebugLoggingEnabled(IsDebugLoggingEnabled);

                if (logSink == null)
                {
                    global::Datadog.AutoInstrumentation.ManagedLoader.Log.Configure.Error(null);
                    global::Datadog.AutoInstrumentation.ManagedLoader.Log.Configure.Info(null);
                    global::Datadog.AutoInstrumentation.ManagedLoader.Log.Configure.Debug(null);
                }
                else
                {
                    global::Datadog.AutoInstrumentation.ManagedLoader.Log.Configure.Error((component, msg, ex, data) => logSink.Error(StringPair.Create("ManagedLoader", component), msg, ex, data));
                    global::Datadog.AutoInstrumentation.ManagedLoader.Log.Configure.Info((component, msg, data) => logSink.Info(StringPair.Create("ManagedLoader", component), msg, data));
                    global::Datadog.AutoInstrumentation.ManagedLoader.Log.Configure.Debug((component, msg, data) => { if (IsDebugLoggingEnabled) { logSink.Debug(StringPair.Create("ManagedLoader", component), msg, data); } });
                }
            }
        }
    }
}
