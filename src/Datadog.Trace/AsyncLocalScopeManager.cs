using System;
using System.Runtime.CompilerServices;
using Datadog.Trace.Logging;

namespace Datadog.Trace
{
    internal class AsyncLocalScopeManager : ScopeManagerBase
    {
        private readonly AsyncLocalCompat<StrongBox<Scope>> _activeScope = new AsyncLocalCompat<StrongBox<Scope>>();

        public override Scope Active
        {
            get
            {
                return _activeScope.Get()?.Value;
            }

            protected set
            {
                _activeScope.Set(new StrongBox<Scope>(value));
            }
        }

        public override void SetActiveScopeReference(Scope scope)
        {
            StrongBox<Scope> box = _activeScope.Get();
            if (box is null)
            {
                _activeScope.Set(new StrongBox<Scope>(scope));
            }
            else
            {
                box.Value = scope;
            }
        }
    }
}
