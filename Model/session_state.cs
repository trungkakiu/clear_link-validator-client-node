using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerService1.Model
{
    public sealed class SessionContext
    {
        private static readonly Lazy<SessionContext> _instance =
            new Lazy<SessionContext>(() => new SessionContext());

        public static SessionContext Instance => _instance.Value;

        private SessionContext() { }

        public string? SessionId { get; private set; }

        public string? NodeId { get; private set; }

        public bool IsAuthenticated => !string.IsNullOrEmpty(SessionId);

        public void SetSession(string sessionId, string nodeId)
        {
            SessionId = sessionId;
            NodeId = nodeId;
        }

        public void Clear()
        {
            SessionId = null;
            NodeId = null;
        }
    }

}
