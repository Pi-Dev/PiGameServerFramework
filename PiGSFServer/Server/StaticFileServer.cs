using System;
using System.IO;

namespace PiGSF.Server
{
    public class StaticFileServer
    {
        private readonly string _baseDirectory;
        public StaticFileServer(string pathToFileOrDir) => _baseDirectory = pathToFileOrDir;
        public static implicit operator Func<Request, Response>(StaticFileServer server) => server.Invoke;

        public Response Invoke(Request request)
        {
            try
            {
                string relativePath = request.Path.Substring(request.Path.IndexOf("/") + 1); // Remove leading path component
                string filePath = Path.Combine(_baseDirectory, relativePath);
                if (!File.Exists(filePath)) return new Response(404, "text/plain", "File Not Found");
                string contentType = GetContentType(filePath);
                byte[] fileContent = File.ReadAllBytes(filePath);
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
}
