using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

namespace PiGSF.Server
{
    public class Request
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> QueryParams { get; set; } = new Dictionary<string, string>();
        public string Body { get; set; }

        public Request(string method, string path, string body = null)
        {
            Method = method.ToUpper();
            Path = path;
            Body = body;
        }
    }

    public class Response
    {
        public int StatusCode { get; set; }
        public string ContentType { get; set; }
        public string Body { get; set; }
        public Dictionary<string, string> ExtraHeaders { get; set; } = new Dictionary<string, string>();
        public void AddHeader(string key, string value) => ExtraHeaders[key] = value;
        public Response(int statusCode, string contentType, string body)
        {
            StatusCode = statusCode;
            ContentType = contentType;
            Body = body;
        }
        public static Response Text(string body, int status=200) => new Response(status, "text/plain", body);
        public static Response Html(string body, int status=200) => new Response(status, "text/html", body);
        public static Response Json(string body, int status=200) => new Response(status, "text/json", body);
        public static Response Json(JsonNode node, int status=200) => new Response(status, "text/json", node.ToJsonString());
    }

    static class RESTManager
    {
        private class PathComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                // Sort by length (descending), then alphabetically
                int lengthComparison = y.Length.CompareTo(x.Length);
                return lengthComparison != 0 ? lengthComparison : string.Compare(x, y, StringComparison.Ordinal);
            }
        }

        private static readonly ReaderWriterLockSlim RouteLock = new ReaderWriterLockSlim();
        public static SortedDictionary<string, Dictionary<string, Func<Request, Response>>> Routes = new(new PathComparer());

        public static void Register(string path, Func<Request, Response> callback) => Register("GET", path, callback);
        public static void Register(string method, string path, Func<Request, Response> callback)
        {
            RouteLock.EnterWriteLock();
            try
            {
                if (!Routes.ContainsKey(path))
                    Routes[path] = new Dictionary<string, Func<Request, Response>>();
                Routes[path][method.ToUpper()] = callback;
            }
            finally
            {
                RouteLock.ExitWriteLock();
            }
        }

        public static void Unregister(string path) => Unregister("GET", path);
        public static void Unregister(string method, string path)
        {
            RouteLock.EnterWriteLock();
            try
            {
                if (Routes.ContainsKey(path))
                {
                    Routes[path].Remove(method.ToUpper());

                    // Remove the path entry if it no longer contains any methods
                    if (Routes[path].Count == 0)
                        Routes.Remove(path);
                }
            }
            finally
            {
                RouteLock.ExitWriteLock();
            }
        }


        private static bool IsPathMatch(string registeredPath, string requestPath)
        {
            if (registeredPath == "/*") return true; // Global wildcard

            var registeredSegments = registeredPath.Split('/');
            var requestSegments = requestPath.Split('/');

            if (registeredSegments.Length > requestSegments.Length) return false;

            for (int i = 0; i < registeredSegments.Length; i++)
            {
                if (registeredSegments[i] == "*") continue; // Wildcard matches any segment
                if (!registeredSegments[i].Equals(requestSegments[i], StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }

        public static Response HandleRequest(Request request)
        {
            RouteLock.EnterReadLock();
            try
            {
                // Prioritize full path matches
                if (Routes.ContainsKey(request.Path) && Routes[request.Path].ContainsKey(request.Method))
                {
                    var r = Routes[request.Path][request.Method](request);
                    return r;
                }
                // Find the most specific wildcard match
                var matchingPaths = Routes.Keys
                    .Where(path => IsPathMatch(path, request.Path))
                    .ToList(); // No need to sort dynamically since it's pre-sorted in Routes

                foreach (var path in matchingPaths)
                    if (Routes[path].ContainsKey(request.Method))
                        return Routes[path][request.Method](request);

                // If no match found, return 404
                return new Response(404, "text/plain", "Not Found");
            }
            finally
            {
                RouteLock.ExitReadLock();
            }
        }

    }
}
