﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Datadog.Trace.Tools.Analyzers {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "4.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Resources {

        private static global::System.Resources.ResourceManager resourceMan;

        private static global::System.Globalization.CultureInfo resourceCulture;

        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }

        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Datadog.Trace.Tools.Analyzers.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }

        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to While blocks are vulnerable to infinite loop on ThreadAbortException due to a bug in the runtime. The catch block should rethrow a ThreadAbortException, or use a finally block.
        /// </summary>
        internal static string ThreadAbortAnalyzerDescription {
            get {
                return ResourceManager.GetString("ThreadAbortAnalyzerDescription", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to Potential infinite loop - you should rethrow Exception in catch block.
        /// </summary>
        internal static string ThreadAbortAnalyzerMessageFormat {
            get {
                return ResourceManager.GetString("ThreadAbortAnalyzerMessageFormat", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to Potential infinite loop on ThreadAbortException.
        /// </summary>
        internal static string ThreadAbortAnalyzerTitle {
            get {
                return ResourceManager.GetString("ThreadAbortAnalyzerTitle", resourceCulture);
            }
        }

        /// <summary>
        ///   Looks up a localized string similar to Rethrow exception.
        /// </summary>
        internal static string ThreadAbortCodeFixTitle {
            get {
                return ResourceManager.GetString("ThreadAbortCodeFixTitle", resourceCulture);
            }
        }
    }
}
