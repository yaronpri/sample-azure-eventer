using System;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace Samples.Azure.Eventer.ServiceGenerator
{
    public class MyTelemetryInitializer : ITelemetryInitializer
    {
        public const string ROLE_NAME = "ServiceGenerator";
        public void Initialize(ITelemetry telemetry)
        {
            if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleName))
            {
                //set custom role name here
                telemetry.Context.Cloud.RoleName = ROLE_NAME;
                telemetry.Context.Cloud.RoleInstance = Environment.MachineName;
            }
        }
    }
}