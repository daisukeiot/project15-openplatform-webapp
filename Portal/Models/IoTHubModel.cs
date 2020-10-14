using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Portal.Models
{
    public class TELEMETRY_DATA
    {
        public string TelemetryName { get; set; }
        public string TelemetryType { get; set; }
        public string TelemetryDisplayName { get; set; }
        public string unit { get; set; }
        public string dataType { get; set; }
    }

    public class COMMAND_DATA
    {
        public string CommandName { get; set; }
        public string CommandDisplayName { get; set; }

        public string CommandDescription { get; set; }

        public string requestName { get; set; }

        public string requestKind { get; set; }
    }
    public class DEVICE_DATA
    {
        public string deviceId { get; set; }
        public string connectionState { get; set; }
        public string status { get; set; }
        public string authenticationType { get; set; }
        public string primaryKey { get; set; }
        public string secondaryKey { get; set; }
        public string connectionString { get; set; }
        public string modelId { get; set; }
        public List<TELEMETRY_DATA> telemetry { get; set; }
    }
}
