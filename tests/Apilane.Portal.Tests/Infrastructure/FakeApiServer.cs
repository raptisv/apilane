using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Portal.Tests.Infrastructure
{
    /// <summary>
    /// One request the Portal sent to the API server.
    /// </summary>
    public record ApiServerRequest(HttpMethod Method, string Url, string Path, Dictionary<string, string> Headers, string Body);

    /// <summary>
    /// Stands in for the API servers: the last handler of the HttpClient the Portal calls them
    /// with. It records every request and answers what the test scripted for the path. Strict:
    /// a request to a path nobody scripted throws, so it fails the test instead of going out on
    /// the network.
    /// </summary>
    public class FakeApiServer : HttpMessageHandler
    {
        private readonly object _lock = new object();
        private readonly List<ApiServerRequest> _requests = new List<ApiServerRequest>();
        private readonly Dictionary<string, Func<HttpResponseMessage>> _answers = new Dictionary<string, Func<HttpResponseMessage>>(StringComparer.OrdinalIgnoreCase);

        public const string ClearCachePath = "/api/Application/ClearCache";
        public const string GetSystemEntitiesPath = "/api/ApplicationNew/GetSystemEntities";
        public const string GeneratePath = "/api/ApplicationNew/Generate";
        public const string GetSystemPropertiesAndConstraintsPath = "/api/Application/GetSystemPropertiesAndConstraints";
        public const string GenerateEntityPath = "/api/Application/GenerateEntity";
        public const string RenameEntityPath = "/api/Application/RenameEntity";
        public const string DegenerateEntityPath = "/api/Application/DegenerateEntity";
        public const string GeneratePropertyPath = "/api/Application/GenerateProperty";
        public const string RenameEntityPropertyPath = "/api/Application/RenameEntityProperty";
        public const string DegeneratePropertyPath = "/api/Application/DegenerateProperty";
        public const string GenerateConstraintsPath = "/api/Application/GenerateConstraints";
        public const string StatsDistinctPath = "/api/Stats/Distinct";

        /// <summary>
        /// The requests received since the last reset, in order.
        /// </summary>
        public List<ApiServerRequest> Requests
        {
            get
            {
                lock (_lock)
                {
                    return _requests.ToList();
                }
            }
        }

        public List<ApiServerRequest> RequestsTo(string path)
        {
            return Requests.Where(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Forgets the recorded requests and the scripted answers.
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _requests.Clear();
                _answers.Clear();
            }
        }

        /// <summary>
        /// Answers every request to the path with this status and body, until the next reset.
        /// </summary>
        public void Respond(string path, HttpStatusCode status, string body, string mediaType = "application/json")
        {
            Script(path, () => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
            });
        }

        /// <summary>
        /// Makes every request to the path fail the way an unreachable server does.
        /// </summary>
        public void Unreachable(string path)
        {
            Script(path, () => throw new HttpRequestException("No such host is known."));
        }

        /// <summary>
        /// Makes every request to the path fail the way a request that timed out does.
        /// </summary>
        public void TimesOut(string path)
        {
            Script(path, () => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("The request has no URL.");
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            var recorded = new ApiServerRequest(
                request.Method,
                uri.AbsoluteUri,
                uri.AbsolutePath,
                request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value), StringComparer.OrdinalIgnoreCase),
                body);

            Func<HttpResponseMessage>? answer;

            lock (_lock)
            {
                _requests.Add(recorded);
                _answers.TryGetValue(uri.AbsolutePath, out answer);
            }

            if (answer is null)
            {
                throw new InvalidOperationException($"The test did not script an answer for {request.Method} {uri.AbsoluteUri}.");
            }

            return answer();
        }

        private void Script(string path, Func<HttpResponseMessage> answer)
        {
            lock (_lock)
            {
                _answers[path] = answer;
            }
        }
    }
}
