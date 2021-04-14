using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Samples.AspNetCoreSimpleController
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine(typeof(Datadog.Trace.ClrProfiler.Instrumentation).Assembly.FullName);
            Console.WriteLine();
            Console.WriteLine("Environment Variables:");
            Console.WriteLine();

            List<KeyValuePair<string, string>> lstKeyValue = new List<KeyValuePair<string, string>>();

            foreach (DictionaryEntry item in Environment.GetEnvironmentVariables())
            {
                lstKeyValue.Add(new KeyValuePair<string, string>(item.Key?.ToString(), item.Value?.ToString()));
            }

            foreach (KeyValuePair<string, string> item in lstKeyValue.OrderBy(i => i.Key))
            {
                Console.WriteLine($"   {item.Key}={item.Value}");
            }

            Console.WriteLine();
            Console.WriteLine("Running...");
            Console.WriteLine();
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
