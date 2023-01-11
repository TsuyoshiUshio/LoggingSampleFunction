using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Xml.Linq;
using System.Net.Http;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace LoggingSampleFunction
{
    public class Function1
    {
        private int MAX_WORKER_COUNT = 3;
        private static readonly ConcurrentDictionary<string, string> workerPool;
        private static readonly ConcurrentDictionary<string, string> workerAssignementPerSite = new ConcurrentDictionary<string, string>();

        private int THROW_EXCEPTION_COUNT_EACH = 5;
        private static object _lock = new object();
        private static long workerExecutionCount = 0;

        private static object lastLogTimeLock = new object();
        private static DateTime lastLogTime = DateTime.UtcNow;

        private static long assignmentCount = 0;

        private HttpClient client;

        static Function1()
        {
            // SUDO implementaion for demo
            workerPool = new ConcurrentDictionary<string, string>();
            workerPool.TryAdd("worker01", "100.100.100.101");
            workerPool.TryAdd("worker02", "100.100.100.102");
            workerPool.TryAdd("worker03", "100.100.100.103");
        }

        public Function1(IHttpClientFactory factory)
        {
            client = factory.CreateClient();
        }

        [FunctionName("FrontEnd")]
        public async Task<IActionResult> StartAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {

            log.LogInformation($"FrontEnd Received a request.");
            if (ReadyForLogging())
            {
                log.LogInformation($"LoggingSampleFunction version: {typeof(Function1).Assembly.GetName().Version}");
            }

            string name = req.Query["siteName"];
            if (string.IsNullOrEmpty(name))
            {
                log.LogWarning($"Can not find siteName: {name}. Pass a siteName in the query string for a personalized response.");
                return new BadRequestResult();
            }

            // Ask the assignment 
            var response = await client.PutAsync($"{GetHostName(req)}/api/Assignment?{name}", new StringContent(string.Empty));
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning($"Can not obtain a worker from Assignment. StatusCode: {response.StatusCode}");
                // Too many request
                return new ObjectResult("Too many requests.") { StatusCode = 429 };
            }
            var workerName = await response.Content.ReadAsStringAsync();

            response = await client.GetAsync($"{GetHostName(req)}/api/Worker?siteName={name}&workerName={workerName}");
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning($"Worker fails to process the request. StatusCode: {response.StatusCode}");
                return new ObjectResult("Too many requests.") { StatusCode = 429 };
            }

            return new OkObjectResult(await response.Content.ReadAsStringAsync());
        }

        private string GetHostName(HttpRequest req)
        {
            return $"{req.Scheme}://{req.Host.Host}:{req.Host.Port}";
        }

        private bool ReadyForLogging()
        {
            lock (lastLogTimeLock)
            {
                if (lastLogTime + TimeSpan.FromMinutes(10) < DateTime.UtcNow)
                {
                    lastLogTime = DateTime.UtcNow;
                    return true;
                }
            }
            return false;
        }

        [FunctionName("Assignment")]
        public IActionResult Assignment([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = null)] HttpRequest req,
            ILogger log)
        {
            string siteName = req.Query["siteName"];

            // search WorkerPool and find not asssigend ones
            // try add. it could fails. in this case, retry 3 times. 
            string workerName = default;

            if (RetryUtils.TryRetry("workerAssignment", () =>
            {
                if (TryGetWorkerName(out workerName))
                {
                    return workerAssignementPerSite.TryAdd(workerName, siteName);
                }
                return false;
            }, 3, log))
            {
                log.LogInformation($"Worker {workerName} is assigned to the site: {siteName}");
                // worker assignment done
                return new OkObjectResult(workerName);
            }
            else
            {
                // fails to assign worker.
                return new ObjectResult("Service Unavailable.") { StatusCode = 503 };
            }
        }

        private bool TryGetWorkerName(out string workerName)
        {
            // Sudo Round Robbin.
            var currentCount = Interlocked.Increment(ref assignmentCount);
            var workerNames = workerPool.Keys.Where(p => !workerAssignementPerSite.Keys.Contains(p)).ToArray();
            var selectedIndex = (workerNames.Length + currentCount) % workerNames.Length;
            workerName = workerNames[selectedIndex];
            return !string.IsNullOrEmpty(workerName);
        }

        [FunctionName("Worker")]
        public async Task<IActionResult> ProcessAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            string siteName = req.Query["siteName"];
            string workerName = req.Query["workerName"];
            log.LogInformation($"Processing worker workerName: {workerName} siteName: {siteName}");
            try
            {
                lock (_lock)
                {
                    workerExecutionCount++;
                    if (workerExecutionCount % THROW_EXCEPTION_COUNT_EACH == 0)
                    {
                        throw new Exception("Some known exception");
                    }
                }
            }
            catch (Exception e)
            {
                // NOTE: this message is for demo. maximum_worker_assigment is not exists on the host.json.
                log.LogError($"Can not process worker by exceed the threshold. Configure maxinmum_worker_assignment on host.json. For more details https://aka.ms/host.json, Message: {e.Message} StackTrace: {e.StackTrace}");
                throw;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
            if (workerAssignementPerSite.TryRemove(workerName, out string siteNameAssigned))
            {
                // ok
                log.LogInformation($"Successfully finish processing siteName: {siteName} workerName: {workerName}");
                return new OkObjectResult($"Hello world from {workerName} for {siteName}");
            }
            else
            {
                // should not be. It is consider to be wrong state. 
                log.LogError($"Can not remove worker. A worker duplication assignment happens. Share the exception to the support team.");
                return new ObjectResult("Service Unavailable.") { StatusCode = 503 };
            }

        }
    }
}