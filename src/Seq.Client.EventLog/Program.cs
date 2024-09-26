using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using Serilog;

namespace Seq.Client.EventLog
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// The service can be installed or uninstalled from the command line
        /// by passing the /install or /uninstall argument, and can be run
        /// interactively by specifying the path to the JSON configuration file.
        /// </summary>
        

        public static RawEvents ToDto(this EventRecord entry, string logName)
        {
            return new RawEvents
            {
                Events = new[]
                {
                    new RawEvent
                    {
                        Timestamp = entry.TimeCreated ?? DateTime.MinValue,
                        Level = MapLevel(entry),
                        MessageTemplate = entry.FormatDescription(),
                        Properties = new Dictionary<string, object>
                        {
                            { "MachineName", entry.MachineName },
#pragma warning disable 618
                            { "EventId", entry.Id },
#pragma warning restore 618
                            { "InstanceId", entry.MachineName },
                            { "Source", entry.ProviderName },
                            //{ "Category", entry. },
                            { "EventLogName", logName }
                        }
                    },
                }
            };

            string MapLevel(EventRecord eventRecord)
            {
                string ret;
                try
                {
                    ret = eventRecord.LevelDisplayName;
                    return ret;
                }
                catch
                {
                    
                }

                switch (eventRecord.Level)
                {
                    case 4: return "Information";
                    case 3: return "Warning";
                    case 2: return "Error";
                    case 1: return "Fatal";
                }

                throw new NotImplementedException();
            }
        }

        public static void Main(string[] args)
        {
            string[] filePaths;
            if (File.Exists(args[0]))
            {
                filePaths = new[] { args[0] };
            }
            else if (Directory.Exists(args[0]))
            {
                filePaths = Directory.EnumerateFiles(args[0], "*.evtx", SearchOption.AllDirectories).ToArray();
            }
            else throw new InvalidOperationException();

            foreach (var filePath in filePaths)
            {
                var logName = Path.GetFileName(filePath);
                EventLogReader reader = new EventLogReader(filePath, PathType.FilePath);
                EventRecord record;
                while ((record = reader.ReadEvent()) != null)
                {
                    SeqApi.PostRawEvents(record.ToDto(logName)).Wait();
                }
            }
        }

        static void RunInteractive(string configFilePath)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            try
            {
                Log.Information("Running interactively");

                var client = new EventLogClient();
                client.Start(configFilePath);

                var done = new ManualResetEvent(false);
                Console.CancelKeyPress += (s, e) =>
                {
                    Log.Information("Ctrl+C pressed, stopping");
                    client.Stop();
                    done.Set();
                };

                done.WaitOne();
                Log.Information("Stopped");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "An unhandled exception occurred");
                Environment.ExitCode = 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
