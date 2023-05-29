// Copyright 2022 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// ReSharper disable ExplicitCallerInfoArgument

#nullable enable

using Serilog;
using System.Diagnostics;
using Newtonsoft.Json;
using NLog;
using Serilog.Sinks.OpenTelemetry;
using ILogger = Serilog.ILogger;
#pragma warning disable CS0168

namespace Example;

static class Program
{
    static readonly Random Rand = new();
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    static void Main()
    {
        
        // create an ActivitySource (that is listened to) for creating an Activity
        // to test the trace and span ID enricher
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };

        ActivitySource.AddActivityListener(listener);

        var source = new ActivitySource("test.example", "1.0.0");

        // Create the loggers to send to gRPC and to HTTP.
        
        //var grpcLogger = GetLogger(OtlpProtocol.GrpcProtobuf);
        //var httpLogger = GetLogger(OtlpProtocol.HttpProtobuf);

        using (source.StartActivity("grpc-loop")?
                   .Start()
                   .AddBaggage("airport", "SYD")
                   .AddBaggage("airline", "QF"))
        {
            //System.Diagnostics.Activity.Current?.AddBaggage("airport", "SYD");

            SendLogs(null, "grpc/protobuf");
            //SendLogs(grpcLogger, "grpc/protobuf");
            //Logger.Debug("Debug Log");
            //Logger.Info("Hallo {name} from NLog", "world");

            try
            {
                throw new ArgumentException("This is an invalid argument", "name");
            }
            catch (Exception e)
            {
                //Logger.Error(e, "Testing an error log");
            }
        }

        //using (source.StartActivity("http-loop"))
        //{
        //    SendLogs(httpLogger, "http/protobuf");
        //}

        Thread.Sleep(5000);
    }

    static void SendLogs(ILogger? logger, string protocol)
    {
        var position = new { Latitude = Rand.Next(-90, 91), Longitude = Rand.Next(0, 361) };
        var elapsedMs = Rand.Next(0, 101);
        var roll = Rand.Next(0, 7);

        var person = new Person
        {
            Name = "foo",
            Position = new Position
            {
                Latitude = position.Latitude,
                Longitude = position.Longitude
            }
        };

        if (logger != null)
        {
            var log = logger
                .ForContext("Elapsed", elapsedMs)
                .ForContext("Protocol", protocol)

                .ForContext("telemetry.sdk.name", "serilog.otel")
                .ForContext("telemetry.sdk.language", "csharp")
                .ForContext("telemetry.sdk.version", "1.0.0")
                .ForContext("ddtags","foo:boo");

            //log.Information("The position is {@Position}", position);
            log.Information("Welcome from Serilog: {@person}", person);
        }

//        Logger.Info("Testing simple value: name: {name}, age: {age}", "foo", 30);
//        Logger.Info("Testing json {person}", JsonConvert.SerializeObject(person));

        //Logger.Info("NLog Hallo Person with _ {person}", person);
        Logger.Info("NLog Hallo Person with @ {@person}", person);
        Logger.Info("Logging a number: {0}", 10);
        //Logger.Info("NLog Hallo Person with $ {$person}", person);
        //Logger.Info("NLog Hallo Person with 0 {0}", person);
        //Logger.Info("NLog Hallo Person {person}", "foo");


        try
        {
            throw new Exception(protocol);
        }
        catch (Exception ex)
        {
            logger?.ForContext("protocol", protocol).Error(ex, "Error on roll {Roll}", roll);
        }
    }

    /*
    static ILogger GetLogger(OtlpProtocol protocol)
    {
        var port = protocol == OtlpProtocol.HttpProtobuf ? 4318 : 4317;
        var endpoint = $"http://localhost:{port}/v1/logs";

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.OpenTelemetry(options => {
                options.Endpoint = endpoint;
                options.Protocol = protocol;
                options.IncludedData = 
                    IncludedData.SpanIdField
                    | IncludedData.TraceIdField
                    | IncludedData.MessageTemplateTextAttribute
                    | IncludedData.MessageTemplateMD5HashAttribute;
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = "test-logging-service",
                    ["service.version"] = "1.2.3",
                    ["telemetry.sdk.name"] = "serilog.otel",
                    ["telemetry.sdk.language"] = "csharp.otel",
                    ["telemetry.sdk.version"] = "1.0.0",
                    ["ddsource"] = "csharp",
                    ["index"] = 10,
                    ["flag"] = true,
                    ["pi"] = 3.14,
                    ["deployment.environment"] = "local"
                };
                options.Headers = new Dictionary<string, string>
                {
                  //  ["Authorization"] = "Basic dXNlcjphYmMxMjM=", // user:abc123
                };
                options.BatchingOptions.BatchSizeLimit = 2;
                options.BatchingOptions.Period = TimeSpan.FromSeconds(2);
                options.BatchingOptions.QueueLimit = 10;
            })
            .CreateLogger();
    }
    */

    public class Position
    {
        public int Latitude { get; set; }

        public int Longitude { get; set; }
    }

    public class Person
    {
        public string? Name { get; set; }

        public Position? Position { get; set; }
    }
}
