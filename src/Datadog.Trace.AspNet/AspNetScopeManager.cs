using System;
using System.Runtime.CompilerServices;
using System.Web;
using Datadog.Trace.Logging;

namespace Datadog.Trace.AspNet
{
    internal class AspNetScopeManager : ScopeManagerBase
    {
        private readonly string _name = "__Datadog_Scope_Current__" + Guid.NewGuid();
        private readonly AsyncLocalCompat<StrongBox<Scope>> _activeScopeFallback = new AsyncLocalCompat<StrongBox<Scope>>();

        public override Scope Active
        {
            get
            {
                var activeScope = _activeScopeFallback.Get()?.Value;
                if (activeScope != null)
                {
                    return activeScope;
                }

                return HttpContext.Current?.Items[_name] as Scope;
            }

            protected set
            {
                _activeScopeFallback.Set(new StrongBox<Scope>(value));

                var httpContext = HttpContext.Current;
                if (httpContext != null)
                {
                    httpContext.Items[_name] = value;
                }
            }
        }

        public override void SetActiveScopeReference(Scope scope)
        {
            StrongBox<Scope> box = _activeScopeFallback.Get();
            if (box is null)
            {
                _activeScopeFallback.Set(new StrongBox<Scope>(scope));
            }
            else
            {
                box.Value = scope;
            }

            var httpContext = HttpContext.Current;
            if (httpContext != null)
            {
                httpContext.Items[_name] = scope;
            }
        }
    }
}
