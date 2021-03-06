using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Datadog.Trace.Ci;
using Datadog.Trace.ClrProfiler.Emit;
using Datadog.Trace.ExtensionMethods;
using Datadog.Trace.Logging;

namespace Datadog.Trace.ClrProfiler.Integrations.Testing
{
    /// <summary>
    /// Tracing integration for NUnit teting framework
    /// </summary>
    public static class NUnitIntegration
    {
        private const string IntegrationName = "NUnit";
        private const string Major3 = "3";
        private const string Major3Minor0 = "3.0";

        private const string NUnitAssembly = "nunit.framework";

        private const string NUnitTestCommandType = "NUnit.Framework.Internal.Commands.TestCommand";
        private const string NUnitTestMethodCommandType = "NUnit.Framework.Internal.Commands.TestMethodCommand";
        private const string NUnitSkipCommandType = "NUnit.Framework.Internal.Commands.SkipCommand";
        private const string NUnitExecuteMethod = "Execute";

        private const string NUnitWorkShiftType = "NUnit.Framework.Internal.Execution.WorkShift";
        private const string NUnitShutdownMethod = "ShutDown";

        private const string NUnitTestResultType = "NUnit.Framework.Internal.TestResult";
        private const string NUnitTestExecutionContextType = "NUnit.Framework.Internal.TestExecutionContext";

        private static readonly IDatadogLogger Log = DatadogLogging.GetLoggerFor(typeof(NUnitIntegration));

        /// <summary>
        /// Wrap the original NUnit.Framework.Internal.Commands.TestMethodCommand.Execute method by adding instrumentation code around it
        /// </summary>
        /// <param name="testMethodCommand">The test method command instance</param>
        /// <param name="testExecutionContext">Test execution context</param>
        /// <param name="opCode">The OpCode used in the original method call.</param>
        /// <param name="mdToken">The mdToken of the original method call.</param>
        /// <param name="moduleVersionPtr">A pointer to the module version GUID.</param>
        /// <returns>The original method's return value.</returns>
        [InterceptMethod(
            TargetAssembly = NUnitAssembly,
            TargetType = NUnitTestCommandType,
            TargetMethod = NUnitExecuteMethod,
            TargetMinimumVersion = Major3Minor0,
            TargetMaximumVersion = Major3,
            TargetSignatureTypes = new[] { NUnitTestResultType, NUnitTestExecutionContextType })]
        public static object TestCommand_Execute(
            object testMethodCommand,
            object testExecutionContext,
            int opCode,
            int mdToken,
            long moduleVersionPtr)
        {
            if (testMethodCommand == null) { throw new ArgumentNullException(nameof(testMethodCommand)); }

            Type testMethodCommandType = testMethodCommand.GetType();
            Func<object, object, object> execute;

            try
            {
                execute = MethodBuilder<Func<object, object, object>>
                    .Start(moduleVersionPtr, mdToken, opCode, NUnitExecuteMethod)
                    .WithConcreteType(testMethodCommandType)
                    .WithParameters(testExecutionContext)
                    .WithNamespaceAndNameFilters(NUnitTestResultType, NUnitTestExecutionContextType)
                    .Build();
            }
            catch (Exception ex)
            {
                Log.ErrorRetrievingMethod(
                    exception: ex,
                    moduleVersionPointer: moduleVersionPtr,
                    mdToken: mdToken,
                    opCode: opCode,
                    instrumentedType: NUnitTestCommandType,
                    methodName: NUnitExecuteMethod,
                    instanceType: testMethodCommandType.AssemblyQualifiedName);
                throw;
            }

            if (testMethodCommandType.FullName != NUnitTestMethodCommandType &&
                testMethodCommandType.FullName != NUnitSkipCommandType)
            {
                return execute(testMethodCommand, testExecutionContext);
            }

            Scope scope = CreateScope(testExecutionContext, testMethodCommandType);
            if (scope is null)
            {
                return execute(testMethodCommand, testExecutionContext);
            }

            using (scope)
            {
                object result = null;
                Exception exception = null;
                try
                {
                    scope.Span.ResetStartTime();
                    result = execute(testMethodCommand, testExecutionContext);
                }
                catch (Exception ex)
                {
                    exception = ex;
                    throw;
                }
                finally
                {
                    FinishScope(scope, testExecutionContext, exception);
                }

                return result;
            }
        }

        private static Scope CreateScope(object testExecutionContext, Type testMethodCommandType)
        {
            Scope scope = null;

            try
            {
                if (testExecutionContext.TryGetPropertyValue<object>("CurrentTest", out object currentTest))
                {
                    MethodInfo testMethod = null;
                    object[] testMethodArguments = null;
                    object properties = null;
                    string testCaseName = null;

                    if (currentTest != null)
                    {
                        if (currentTest.TryGetPropertyValue<object>("Method", out object method))
                        {
                            method?.TryGetPropertyValue<MethodInfo>("MethodInfo", out testMethod);
                        }

                        currentTest.TryGetPropertyValue<object[]>("Arguments", out testMethodArguments);
                        currentTest.TryGetPropertyValue<object>("Properties", out properties);

                        currentTest.TryGetPropertyValue<string>("Name", out testCaseName);
                    }

                    if (testMethod != null)
                    {
                        string testFramework = "NUnit " + testMethodCommandType.Assembly.GetName().Version;
                        string testSuite = testMethod.DeclaringType?.FullName;
                        string testName = testMethod.Name;
                        string skipReason = null;
                        Dictionary<string, List<string>> testTraits = null;

                        // Get test parameters
                        TestParameters testParameters = null;
                        ParameterInfo[] methodParameters = testMethod.GetParameters();
                        if (methodParameters?.Length > 0)
                        {
                            testParameters = new TestParameters();
                            testParameters.Metadata = new Dictionary<string, object>();
                            testParameters.Arguments = new Dictionary<string, object>();
                            testParameters.Metadata[TestTags.MetadataTestName] = testCaseName;

                            for (int i = 0; i < methodParameters.Length; i++)
                            {
                                if (testMethodArguments != null && i < testMethodArguments.Length)
                                {
                                    testParameters.Arguments[methodParameters[i].Name] = testMethodArguments[i]?.ToString() ?? "(null)";
                                }
                                else
                                {
                                    testParameters.Arguments[methodParameters[i].Name] = "(default)";
                                }
                            }
                        }

                        // Get traits
                        if (properties != null)
                        {
                            properties.TryCallMethod<string, string>("Get", "_SKIPREASON", out skipReason);

                            if (properties.TryGetFieldValue<Dictionary<string, IList>>("inner", out Dictionary<string, IList> traits) && traits.Count > 0)
                            {
                                testTraits = new Dictionary<string, List<string>>();

                                foreach (KeyValuePair<string, IList> traitValue in traits)
                                {
                                    if (traitValue.Key == "_SKIPREASON")
                                    {
                                        continue;
                                    }

                                    List<string> lstValues = new List<string>();
                                    if (traitValue.Value != null)
                                    {
                                        foreach (object valObj in traitValue.Value)
                                        {
                                            if (valObj is null)
                                            {
                                                continue;
                                            }

                                            lstValues.Add(valObj.ToString());
                                        }
                                    }

                                    testTraits[traitValue.Key] = lstValues;
                                }
                            }
                        }

                        scope = Common.TestTracer.StartActive("nunit.test", serviceName: Common.ServiceName);
                        Span span = scope.Span;

                        span.Type = SpanTypes.Test;
                        span.SetTraceSamplingPriority(SamplingPriority.AutoKeep);
                        span.ResourceName = $"{testSuite}.{testName}";
                        span.SetTag(TestTags.Suite, testSuite);
                        span.SetTag(TestTags.Name, testName);
                        span.SetTag(TestTags.Framework, testFramework);
                        span.SetTag(TestTags.Type, TestTags.TypeTest);
                        CIEnvironmentValues.DecorateSpan(span);

                        var framework = FrameworkDescription.Instance;

                        span.SetTag(CommonTags.RuntimeName, framework.Name);
                        span.SetTag(CommonTags.RuntimeVersion, framework.ProductVersion);
                        span.SetTag(CommonTags.RuntimeArchitecture, framework.ProcessArchitecture);
                        span.SetTag(CommonTags.OSArchitecture, framework.OSArchitecture);
                        span.SetTag(CommonTags.OSPlatform, framework.OSPlatform);
                        span.SetTag(CommonTags.OSVersion, Environment.OSVersion.VersionString);

                        if (testParameters != null)
                        {
                            span.SetTag(TestTags.Parameters, testParameters.ToJSON());
                        }

                        if (testTraits != null && testTraits.Count > 0)
                        {
                            span.SetTag(TestTags.Traits, Datadog.Trace.Vendors.Newtonsoft.Json.JsonConvert.SerializeObject(testTraits));
                        }

                        if (skipReason != null)
                        {
                            span.SetTag(TestTags.Status, TestTags.StatusSkip);
                            span.SetTag(TestTags.SkipReason, skipReason);
                            span.Finish(TimeSpan.Zero);
                            scope.Dispose();
                            scope = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating or populating scope.");
            }

            return scope;
        }

        private static void FinishScope(Scope scope, object testExecutionContext, Exception ex)
        {
            // unwrap the generic NUnitException
            if (ex != null && ex.GetType().FullName == "NUnit.Framework.Internal.NUnitException")
            {
                ex = ex.InnerException;
            }

            if (ex != null)
            {
                string exTypeName = ex.GetType().FullName;

                if (exTypeName == "NUnit.Framework.SuccessException")
                {
                    scope.Span.SetTag(TestTags.Status, TestTags.StatusPass);
                }
                else if (exTypeName == "NUnit.Framework.IgnoreException")
                {
                    scope.Span.SetTag(TestTags.Status, TestTags.StatusSkip);
                    scope.Span.SetTag(TestTags.SkipReason, ex.Message);
                }
                else
                {
                    scope.Span.SetException(ex);
                    scope.Span.SetTag(TestTags.Status, TestTags.StatusFail);
                }
            }
            else
            {
                scope.Span.SetTag(TestTags.Status, TestTags.StatusPass);
            }
        }

        /// <summary>
        /// Wrap the original NUnit.Framework.Internal.Execution.WorkShift.ShutDown method by adding instrumentation code around it
        /// </summary>
        /// <param name="workShift">The workshift instance</param>
        /// <param name="opCode">The OpCode used in the original method call.</param>
        /// <param name="mdToken">The mdToken of the original method call.</param>
        /// <param name="moduleVersionPtr">A pointer to the module version GUID.</param>
        [InterceptMethod(
            TargetAssembly = NUnitAssembly,
            TargetType = NUnitWorkShiftType,
            TargetMethod = NUnitShutdownMethod,
            TargetMinimumVersion = Major3Minor0,
            TargetMaximumVersion = Major3,
            TargetSignatureTypes = new[] { ClrNames.Void })]
        public static void WorkShift_ShutDown(
            object workShift,
            int opCode,
            int mdToken,
            long moduleVersionPtr)
        {
            if (workShift == null) { throw new ArgumentNullException(nameof(workShift)); }

            Type workShiftType = workShift.GetType();
            Action<object> execute;

            try
            {
                execute = MethodBuilder<Action<object>>
                    .Start(moduleVersionPtr, mdToken, opCode, NUnitShutdownMethod)
                    .WithConcreteType(workShiftType)
                    .WithNamespaceAndNameFilters(ClrNames.Void)
                    .Build();
            }
            catch (Exception ex)
            {
                Log.ErrorRetrievingMethod(
                    exception: ex,
                    moduleVersionPointer: moduleVersionPtr,
                    mdToken: mdToken,
                    opCode: opCode,
                    instrumentedType: NUnitWorkShiftType,
                    methodName: NUnitShutdownMethod,
                    instanceType: workShiftType.AssemblyQualifiedName);
                throw;
            }

            execute(workShift);
            SynchronizationContext context = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                // We have to ensure the flush of the buffer after we finish the tests of an assembly.
                // For some reason, sometimes when all test are finished none of the callbacks to handling the tracer disposal is triggered.
                // So the last spans in buffer aren't send to the agent.
                // Other times we reach the 500 items of the buffer in a sec and the tracer start to drop spans.
                // In a test scenario we must keep all spans.
                Common.TestTracer.FlushAsync().GetAwaiter().GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(context);
            }
        }
    }
}
