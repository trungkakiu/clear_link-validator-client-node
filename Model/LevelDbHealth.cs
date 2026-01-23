using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerService1.Model
{
    public class LevelDbHealth
    {
        public bool Alive { get; set; }
        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }
        public double FileSizeMB { get; set; }
        public string Message { get; set; } = "";
    }

}
