﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PiGSF.Server
{
    public class Request
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public string Version { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> QueryParams { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> FormFields { get; set; } = new Dictionary<string, string>();
        public List<MultipartPart> Multipart { get; set; } = new List<MultipartPart>();
        public string Body { get; set; }
        public byte[] BodyBytes { get; set; } = Array.Empty<byte>();

        public Request(string method, string path, string body = null)
        {
            Method = method.ToUpper();
            Path = path;
            Body = body;
        }

        public string? GetHeader(string key)
        {
            return Headers!.GetValueOrDefault(key, null);
        }

        public void ParseStructuredData()
        {
            ParsePathAndQuery();
            var contentType = GetHeader("Content-Type") ?? "";
            if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                ParseUrlEncodedFields(Body, FormFields);
            else if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                ParseMultipartFormData(contentType);
        }

        void ParsePathAndQuery()
        {
            int queryStart = Path.IndexOf('?');
            if (queryStart < 0) return;
            var query = Path.Substring(queryStart + 1);
            Path = Path.Substring(0, queryStart);
            ParseUrlEncodedFields(query, QueryParams);
        }

        static void ParseUrlEncodedFields(string data, Dictionary<string, string> target)
        {
            if (string.IsNullOrEmpty(data)) return;
            var pairs = data.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                int eq = pair.IndexOf('=');
                if (eq < 0) target[DecodeUrl(pair)] = "";
                else target[DecodeUrl(pair.Substring(0, eq))] = DecodeUrl(pair.Substring(eq + 1));
            }
        }

        void ParseMultipartFormData(string contentType)
        {
            string boundary = GetMultipartBoundary(contentType);
            if (string.IsNullOrEmpty(boundary) || BodyBytes == null || BodyBytes.Length == 0) return;

            byte[] delimiter = Encoding.ASCII.GetBytes("--" + boundary);
            int pos = IndexOf(BodyBytes, delimiter, 0);
            while (pos >= 0)
            {
                pos += delimiter.Length;
                if (pos + 1 < BodyBytes.Length && BodyBytes[pos] == '-' && BodyBytes[pos + 1] == '-') break;
                if (pos + 1 < BodyBytes.Length && BodyBytes[pos] == '\r' && BodyBytes[pos + 1] == '\n') pos += 2;

                int headersEnd = IndexOf(BodyBytes, new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' }, pos);
                if (headersEnd < 0) break;

                var partHeaders = ParsePartHeaders(Encoding.ASCII.GetString(BodyBytes, pos, headersEnd - pos));
                int dataStart = headersEnd + 4;
                int next = IndexOf(BodyBytes, delimiter, dataStart);
                if (next < 0) break;
                int dataEnd = next;
                if (dataEnd >= dataStart + 2 && BodyBytes[dataEnd - 2] == '\r' && BodyBytes[dataEnd - 1] == '\n') dataEnd -= 2;

                var data = new byte[Math.Max(0, dataEnd - dataStart)];
                if (data.Length > 0) Buffer.BlockCopy(BodyBytes, dataStart, data, 0, data.Length);

                var part = new MultipartPart { Headers = partHeaders, Data = data };
                part.ContentType = partHeaders.GetValueOrDefault("Content-Type", "");
                ParseContentDisposition(part);
                Multipart.Add(part);
                if (!string.IsNullOrEmpty(part.Name) && string.IsNullOrEmpty(part.FileName))
                    FormFields[part.Name] = part.Text;

                pos = next;
            }
        }

        static Dictionary<string, string> ParsePartHeaders(string headersText)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = headersText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                result[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }
            return result;
        }

        static void ParseContentDisposition(MultipartPart part)
        {
            var cd = part.Headers.GetValueOrDefault("Content-Disposition", "");
            var sections = cd.Split(';');
            foreach (var section in sections)
            {
                var s = section.Trim();
                if (s.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                    part.Name = TrimQuoted(s.Substring(5));
                else if (s.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
                    part.FileName = TrimQuoted(s.Substring(9));
            }
        }

        static string GetMultipartBoundary(string contentType)
        {
            var parts = contentType.Split(';');
            foreach (var part in parts)
            {
                var p = part.Trim();
                if (p.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                    return TrimQuoted(p.Substring(9));
            }
            return "";
        }

        static int IndexOf(byte[] data, byte[] pattern, int start)
        {
            if (pattern.Length == 0) return -1;
            for (int i = start; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                if (match) return i;
            }
            return -1;
        }

        static string DecodeUrl(string value) => Uri.UnescapeDataString(value.Replace("+", " "));

        static string TrimQuoted(string value)
        {
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2);
            return value;
        }
    }

    public class MultipartPart
    {
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public string Name { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string Text => Data == null || Data.Length == 0 ? "" : Encoding.UTF8.GetString(Data);
    }

    public class Response
    {
        public int StatusCode { get; set; }
        public string ContentType { get; set; }
        public string Body { get; set; }
        public byte[] BinaryData { get; set; }
        public Dictionary<string, string> ExtraHeaders { get; set; } = new Dictionary<string, string>();
        public void AddHeader(string key, string value) => ExtraHeaders[key] = value;
        public Response(int statusCode, string contentType, string body)
        {
            StatusCode = statusCode;
            ContentType = contentType;
            Body = body;
        }
        public Response(int statusCode, string contentType, byte[] data)
        {
            StatusCode = statusCode;
            ContentType = contentType;
            BinaryData = data;
        }
        public static Response Binary(byte[] data, int status = 200) => new Response(status, "text/plain", data);
        public static Response Text(string body, int status = 200) => new Response(status, "text/plain", body);
        public static Response Html(string body, int status = 200) => new Response(status, "text/html", body);
        public static Response Json(string body, int status = 200) => new Response(status, "text/json", body);
        //public static Response Json(JsonNode node, int status=200) => new Response(status, "text/json", node.ToJsonString());

        public Response EnableCors(Request req,
            Func<string, bool>? allowOrigin = null,
            bool allowCredentials = false,
            string allowMethods = "*",
            string allowHeaders = "*",
            int maxAgeSeconds = 86400)
        {
            if (req == null) return this;
            var origin = req.GetHeader("Origin");
            if (!string.IsNullOrWhiteSpace(origin) && allowOrigin != null && !allowOrigin(origin)) return this;

            var requestedHeaders = req.GetHeader("Access-Control-Request-Headers");
            ExtraHeaders["Access-Control-Allow-Origin"] = "*";
            ExtraHeaders["Vary"] = "Origin";
            ExtraHeaders["Access-Control-Allow-Methods"] = allowMethods;
            ExtraHeaders["Access-Control-Allow-Headers"] = string.IsNullOrWhiteSpace(requestedHeaders) ? allowHeaders : requestedHeaders;
            ExtraHeaders["Access-Control-Max-Age"] = maxAgeSeconds.ToString();
            if (allowCredentials) ExtraHeaders["Access-Control-Allow-Credentials"] = "true";
            return this;
        }

        public Response EnableCors(Request req, params string[] allowedOrigins)
        {
            if (allowedOrigins == null || allowedOrigins.Length == 0)
                return EnableCors(req, (Func<string, bool>?)null);
            return EnableCors(req, o => allowedOrigins.Contains(o, StringComparer.OrdinalIgnoreCase));
        }

        public static Response CorsPreflight(Request req,
            Func<string, bool>? allowOrigin = null,
            bool allowCredentials = false,
            string allowMethods = "*",
            string allowHeaders = "*",
            int maxAgeSeconds = 86400)
        {
            var r = new Response(204, "text/plain", "");
            r.EnableCors(req, allowOrigin, allowCredentials, allowMethods, allowHeaders, maxAgeSeconds);
            return r;
        }
    }

    public static class RESTManager
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
            if (!path.StartsWith("/")) path = path.Insert(0, "/");
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
            if (!path.StartsWith("/")) path = path.Insert(0, "/");
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

        public static string ResolveHandlerInfo(Request request)
        {
            RouteLock.EnterReadLock();
            try
            {
                if (Routes.ContainsKey(request.Path) && Routes[request.Path].ContainsKey(request.Method))
                    return DescribeHandler(Routes[request.Path][request.Method]);

                var matchingPaths = Routes.Keys
                    .Where(path => IsPathMatch(path, request.Path))
                    .ToList();

                foreach (var path in matchingPaths)
                    if (Routes[path].ContainsKey(request.Method))
                        return DescribeHandler(Routes[path][request.Method]);

                return "(404: No handler)";
            }
            finally
            {
                RouteLock.ExitReadLock();
            }
        }

        static string DescribeHandler(Func<Request, Response> handler)
        {
            if (handler.Target != null)
                return handler.Target.GetType().Name;
            if (handler.Method.DeclaringType != null)
                return handler.Method.DeclaringType.Name;
            return handler.Method.Name;
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
                return new Response(404, "text/plain", "");
            }
            finally
            {
                RouteLock.ExitReadLock();
            }
        }

    }
}
