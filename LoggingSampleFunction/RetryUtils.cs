using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace LoggingSampleFunction
{
    public class RetryUtils
    {
        public static bool TryRetry(string actionName, Func<bool> func, int times, ILogger log)
        {
            int retryCount = 0;
            while (retryCount < times)
            {
                if (!func())
                {
                    log.LogInformation($"Fail to execute {actionName} currentRetryCount {retryCount} retry until {times}");
                    retryCount++;
                }
                else
                {
                    return true;
                }
            }
            log.LogWarning($"Fail to execute {actionName}. Failed {times} times");
            return false;
        }
    }
}
