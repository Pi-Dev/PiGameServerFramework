using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public Response(int statusCode, string body) => new Response(statusCode, "text/plain", body);
        public Response(int statusCode, string contentType, string body)
        {
            StatusCode = statusCode;
            ContentType = contentType;
            Body = body;
        }
    }

    public class DirectoryFileServer
    {
        private readonly string _baseDirectory;

        public DirectoryFileServer(string baseDirectory)
        {
            if (!Directory.Exists(baseDirectory))
                throw new DirectoryNotFoundException($"Directory not found: {baseDirectory}");

            _baseDirectory = baseDirectory;
        }

        public static implicit operator Func<Request, Response>(DirectoryFileServer server)
        {
            return server.Invoke;
        }

        public Response Invoke(Request request)
        {
            try
            {
                string relativePath = request.Path.Substring(request.Path.IndexOf("/") + 1); // Remove leading path component
                string filePath = Path.Combine(_baseDirectory, relativePath);

                if (!File.Exists(filePath))
                {
                    return new Response(404, "text/plain", "File Not Found");
                }

                string contentType = GetContentType(filePath);
                string fileContent = File.ReadAllText(filePath);

                return new Response(200, contentType, fileContent);
            }
            catch (Exception ex)
            {
                return new Response(500, "text/plain", $"Error: {ex.Message}");
            }
        }

        private string GetContentType(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension switch
            {
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".txt" => "text/plain",
                _ => "application/octet-stream",
            };
        }
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
                    return Routes[request.Path][request.Method](request);

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
