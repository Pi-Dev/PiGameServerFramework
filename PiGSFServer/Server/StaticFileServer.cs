using System;
using System.Collections.Generic;
using System.IO;

namespace PiGSF.Server
{
    public class StaticFileServer
    {
        readonly string baseDir;
        readonly string mountPath; // normalized, no leading/trailing '/'
        readonly Func<string, bool> shouldCache; // rel-path policy

        struct CacheEntry
        {
            public byte[] data;
            public long lastWriteUtcTicks;
        }

        readonly Dictionary<string, CacheEntry> memoryFileCache = new(StringComparer.OrdinalIgnoreCase);

        // 1) path, prefix, bool (cache nothing / cache all)
        public StaticFileServer(string pathToDir, string prefix, bool cacheAll)
            : this(pathToDir, prefix, cacheAll ? static _ => true : static _ => false) { }

        // 2) path, prefix, lambda
        public StaticFileServer(string pathToDir, string prefix, Func<string, bool> cachePolicy)
        {
            baseDir = Path.GetFullPath(pathToDir ?? "");
            mountPath = NormalizeMount(prefix);
            shouldCache = cachePolicy ?? (static _ => false);
        }

        // 3) path, bool
        public StaticFileServer(string pathToDir, bool cacheAll)
            : this(pathToDir, "", cacheAll) { }

        // 4) path, lambda
        public StaticFileServer(string pathToDir, Func<string, bool> cachePolicy)
            : this(pathToDir, "", cachePolicy) { }

        public static implicit operator Func<Request, Response>(StaticFileServer s) => s.Invoke;

        public Response Invoke(Request request)
        {
            try
            {
                bool allowRedirects = ServerConfig.Get("HTTPAllowRedirects").ToLower() == "true";

                string reqPath = request?.Path ?? "";
                bool endsWithSlash = reqPath.EndsWith("/") || reqPath == "";

                string rel = GetRelPath(reqPath); // may be ""

                string baseFull = EnsureDirSuffix(Path.GetFullPath(baseDir));
                string full = Path.GetFullPath(Path.Combine(baseFull, rel));

                if (!full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                    return new Response(403, "text/plain", "Forbidden");

                if (Directory.Exists(full))
                {
                    if (!endsWithSlash)
                    {
                        if (!allowRedirects)
                            return new Response(404, "text/plain", "Not Found");

                        return Redirect301(AppendSlash(reqPath));
                    }

                    rel = CombineRel(rel, "index.html");
                    full = Path.GetFullPath(Path.Combine(full, "index.html"));
                }

                if (!File.Exists(full))
                    return new Response(404, "text/plain", "File Not Found");

                string ct = GetContentType(full);

                if (!shouldCache(rel))
                    return new Response(200, ct, File.ReadAllBytes(full));

                var fi = new FileInfo(full);
                long lw = fi.LastWriteTimeUtc.Ticks;

                if (memoryFileCache.TryGetValue(full, out var entry))
                {
                    if (entry.lastWriteUtcTicks == lw && entry.data != null)
                        return new Response(200, ct, entry.data);
                }

                var bytes = File.ReadAllBytes(full);
                memoryFileCache[full] = new CacheEntry
                {
                    data = bytes,
                    lastWriteUtcTicks = lw
                };

                return new Response(200, ct, bytes);
            }
            catch (Exception e)
            {
                return new Response(500, "text/plain", e.Message);
            }
        }

        static Response Redirect301(string location)
        {
            var r = new Response(301, "text/plain", "");
            r.AddHeader("Location: ", location);
            return r;
        }

        static string AppendSlash(string p)
        {
            if (string.IsNullOrEmpty(p)) return "/";
            return p.EndsWith("/") ? p : (p + "/");
        }

        static string CombineRel(string rel, string leaf)
        {
            if (string.IsNullOrEmpty(rel)) return leaf;
            rel = rel.Replace('\\', '/');
            if (!rel.EndsWith("/")) rel += "/";
            return rel + leaf;
        }

        string GetRelPath(string reqPath)
        {
            if (string.IsNullOrEmpty(reqPath)) return "";
            string p = reqPath.TrimStart('/');

            // If framework already passes relative-to-mount, this will just fall through.
            if (!string.IsNullOrEmpty(mountPath) && p.StartsWith(mountPath, StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring(mountPath.Length);
                p = p.TrimStart('/');
            }

            return p.Replace('\\', '/'); // normalize for policy keys
        }

        static string NormalizeMount(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            p = p.Trim();
            while (p.StartsWith("/")) p = p.Substring(1);
            while (p.EndsWith("/")) p = p.Substring(0, p.Length - 1);
            return p;
        }

        static string EnsureDirSuffix(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return Path.DirectorySeparatorChar.ToString();
            char sep = Path.DirectorySeparatorChar;
            if (!dir.EndsWith(sep.ToString())) dir += sep;
            return dir;
        }

        static string GetContentType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".wasm" => "application/wasm",
                ".txt" => "text/plain",
                _ => "application/octet-stream",
            };
        }
    }
}
