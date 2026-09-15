using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Flow.Launcher.Infrastructure.Logger;
using Flow.Launcher.Infrastructure.UserSettings;
using JetBrains.Annotations;

namespace Flow.Launcher.Infrastructure.Http
{
    public static class Http
    {
        private static readonly string ClassName = nameof(Http);

        private const string UserAgent = @"Mozilla/5.0 (Trident/7.0; rv:11.0) like Gecko";

        // Original platform proxy (follows the OS proxy settings on Windows),
        // captured before any override so ProxyMode.System can restore it.
        private static readonly IWebProxy systemDefaultProxy = HttpClient.DefaultProxy;

        private static readonly HttpClient client = new();

        static Http()
        {
            // need to be added so it would work on a win10 machine
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls
                | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        }

        private static HttpProxy proxy;

        public static HttpProxy Proxy
        {
            private get => proxy;
            set
            {
                if (proxy != null)
                    proxy.Changed -= ApplyProxy;

                proxy = value;
                proxy.Changed += ApplyProxy;
                ApplyProxy();
            }
        }

        /// <summary>
        /// The proxy currently assigned to HttpClient.DefaultProxy.
        /// The platform default instance means requests follow the OS proxy settings.
        /// </summary>
        public static IWebProxy CurrentProxy { get; private set; }

        private static void ApplyProxy()
        {
            switch (Proxy.Mode)
            {
                case ProxyMode.Direct:
                    HttpClient.DefaultProxy = CurrentProxy = new WebProxy();
                    break;

                case ProxyMode.Manual:
                    ApplyManualProxy();
                    break;

                default: // ProxyMode.System
                    HttpClient.DefaultProxy = CurrentProxy = systemDefaultProxy;
                    break;
            }
        }

        private static void ApplyManualProxy()
        {
            var endpoint = ProxyResolver.Resolve(Proxy.Address);
            if (endpoint == null)
            {
                // An incomplete manual config must not silently keep any previous
                // proxy or force a direct connection — fall back to the system proxy.
                Log.Warn(ClassName, $"Proxy address <{Proxy.Address}> is invalid or incomplete, falling back to the system proxy");
                HttpClient.DefaultProxy = CurrentProxy = systemDefaultProxy;
                return;
            }

            var webProxy = new WebProxy(new Uri($"{endpoint.Scheme}://{endpoint.Host}:{endpoint.Port}"), true);
            if (!string.IsNullOrEmpty(Proxy.UserName))
                webProxy.Credentials = new NetworkCredential(Proxy.UserName, Proxy.Password);

            HttpClient.DefaultProxy = CurrentProxy = webProxy;
            Log.Debug(ClassName, $"Proxy <{endpoint.Scheme}://{endpoint.Host}:{endpoint.Port}> applied");
        }

        public static async Task DownloadAsync([NotNull] string url, [NotNull] string filePath, Action<double> reportProgress = null, CancellationToken token = default)
        {
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var canReportProgress = totalBytes != -1;

                    if (canReportProgress && reportProgress != null)
                    {
                        await using var contentStream = await response.Content.ReadAsStreamAsync(token);
                        await using var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true);

                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int read;
                        double progressValue = 0;

                        reportProgress(0);

                        while ((read = await contentStream.ReadAsync(buffer, token)) > 0)
                        {
                            await fileStream.WriteAsync(buffer.AsMemory(0, read), token);
                            totalRead += read;

                            progressValue = totalRead * 100.0 / totalBytes;

                            if (token.IsCancellationRequested)
                                return;
                            else
                                reportProgress(progressValue);
                        }

                        if (progressValue < 100)
                            reportProgress(100);
                    }
                    else
                    {
                        await using var fileStream = new FileStream(filePath, FileMode.CreateNew);
                        await response.Content.CopyToAsync(fileStream, token);
                    }
                }
                else
                {
                    throw new HttpRequestException($"Error code <{response.StatusCode}> returned from <{url}>");
                }
            }
            catch (HttpRequestException e)
            {
                Log.Exception(ClassName, "Http Request Error", e, "DownloadAsync");
                throw;
            }
        }

        /// <summary>
        /// Asynchrously get the result as string from url.
        /// When supposing the result larger than 83kb, try using GetStreamAsync to avoid reading as string
        /// </summary>
        /// <param name="url"></param>
        /// <returns>The Http result as string. Null if cancellation requested</returns>
        public static Task<string> GetAsync([NotNull] string url, CancellationToken token = default)
        {
            Log.Debug(ClassName, $"Url <{url}>");
            return GetAsync(new Uri(url), token);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="url"></param>
        /// <param name="token"></param>
        /// <returns>The Http result as string. Null if cancellation requested</returns>
        public static async Task<string> GetAsync([NotNull] Uri url, CancellationToken token = default)
        {
            Log.Debug(ClassName, $"Url <{url}>");
            using var response = await client.GetAsync(url, token);
            var content = await response.Content.ReadAsStringAsync(token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException(
                    $"Error code <{response.StatusCode}> with content <{content}> returned from <{url}>");
            }

            return content;
        }

        /// <summary>
        /// Send a GET request to the specified Uri with an HTTP completion option and a cancellation token as an asynchronous operation.
        /// </summary>
        /// <param name="url">The Uri the request is sent to.</param>
        /// <param name="completionOption">An HTTP completion option value that indicates when the operation should be considered completed.</param>
        /// <param name="token">A cancellation token that can be used by other objects or threads to receive notice of cancellation</param>
        /// <returns></returns>
        public static Task<Stream> GetStreamAsync([NotNull] string url,
            CancellationToken token = default) => GetStreamAsync(new Uri(url), token);

        /// <summary>
        /// Send a GET request to the specified Uri with an HTTP completion option and a cancellation token as an asynchronous operation.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public static async Task<Stream> GetStreamAsync([NotNull] Uri url,
            CancellationToken token = default)
        {
            Log.Debug(ClassName, $"Url <{url}>");
            return await client.GetStreamAsync(url, token);
        }

        public static async Task<HttpResponseMessage> GetResponseAsync(string url, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            CancellationToken token = default)
            => await GetResponseAsync(new Uri(url), completionOption, token);

        public static async Task<HttpResponseMessage> GetResponseAsync([NotNull] Uri url, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            CancellationToken token = default)
        {
            Log.Debug(ClassName, $"Url <{url}>");
            return await client.GetAsync(url, completionOption, token);
        }

        /// <summary>
        /// Asynchrously send an HTTP request.
        /// </summary>
        public static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead, CancellationToken token = default)
        {
            try
            {
                return await client.SendAsync(request, completionOption, token);
            }
            catch (System.Exception)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
        }

        public static async Task<string> GetStringAsync(string url, CancellationToken token = default)
        {
            try
            {
                Log.Debug(ClassName, $"Url <{url}>");
                return await client.GetStringAsync(url, token);
            }
            catch (System.Exception e)
            {
                return string.Empty;
            }
        }
    }
}
