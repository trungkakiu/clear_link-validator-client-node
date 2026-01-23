using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerService1.Model
{
    public class NodeRuntimeState
    {
        public bool SyncRequestInFlight { get; set; }

        public int SyncRetryCount { get; set; }

        public Boolean BlockReconnect { get; set; }

        public DateTime LastSyncRequestAt { get; set; }

        public DateTime? SyncCooldownUntil { get; set; }

        public DateTime LastSyncAttemptAt { get; set; } = DateTime.MinValue;

    }

}
