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

//#if NET5_0_OR_GREATER

using System.Net.Http;
using Grpc.Core;
using Microsoft.Extensions.Options;

#if NETSTANDARD2_1 || NET5_0_OR_GREATER
using Grpc.Net.Client;
#endif
using OpenTelemetry.Proto.Collector.Logs.V1;


namespace Serilog.Sinks.OpenTelemetry;

/// <summary>
/// Implements an IExporter that sends OpenTelemetry Log requests
/// over gRPC.
/// </summary>
sealed class GrpcExporter : IExporter, IDisposable
{
    readonly LogsService.LogsServiceClient _client;
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
    readonly GrpcChannel _channel;
#else
    readonly Channel _channel;
#endif
    readonly Metadata _headers;

    /// <summary>
    /// Creates a new instance of a GrpcExporter that writes an
    /// ExportLogsServiceRequest to a gRPC endpoint.
    /// </summary>
    /// <param name="endpoint">
    /// The full OTLP endpoint to which logs are sent.
    /// </param>
    /// <param name="headers">
    /// A dictionary containing the request headers.
    /// </param>
    /// <param name="httpMessageHandler">
    /// Custom HTTP message handler.
    /// </param>
    public GrpcExporter(string endpoint, IDictionary<string, string>? headers,
        HttpMessageHandler? httpMessageHandler = null)
    {
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
        var grpcChannelOptions = new GrpcChannelOptions();

        if (httpMessageHandler != null)
        {
            grpcChannelOptions.HttpClient = new HttpClient(httpMessageHandler);
            grpcChannelOptions.DisposeHttpClient = true;
        };

        _channel = GrpcChannel.ForAddress(endpoint, grpcChannelOptions);
        _client = new LogsService.LogsServiceClient(_channel);
        _headers = new Metadata();
#else
        var endpointUri = new Uri(endpoint);
        ChannelCredentials channelCredentials;
        if (endpointUri.Scheme == Uri.UriSchemeHttps)
        {
            channelCredentials = new SslCredentials();
        }
        else
        {
            channelCredentials = ChannelCredentials.Insecure;
        }

        _channel = new Channel(endpointUri.Authority, channelCredentials);
        _client = new LogsService.LogsServiceClient(_channel);
        _headers = new Metadata();
#endif
        if (headers != null)
        {
            foreach (var header in headers)
            {
                _headers.Add(header.Key, header.Value);
            }
        }
    }

    public void Dispose()
    {
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
        _channel.Dispose();
#else
        //_channel.ShutdownAsync().GetAwaiter();
#endif
    }

    public void Export(ExportLogsServiceRequest request)
    {
        _client.Export(request, _headers);
    }

    public Task ExportAsync(ExportLogsServiceRequest request)
    {
        return _client.ExportAsync(request, _headers).ResponseAsync;
    }
}

//#endif