using PiGSF.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PiGSF.Server
{
    internal class HTTPProtocol : IProtocol
    {
        enum ParseState
        {
            Method,
            Path,
            Version,
            RequestLineLF,
            HeaderName,
            HeaderValueStart,
            HeaderValue,
            HeaderLineLF,
            HeadersEndLF,
            Body
        }

        TcpTransport.ClientState state;
        ParseState parseState = ParseState.Method;
        StringBuilder method = new();
        StringBuilder path = new();
        StringBuilder version = new();
        StringBuilder headerName = new();
        StringBuilder headerValue = new();
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        int contentLength;
        int bodyRemaining;
        MemoryStream body = new();

        internal HTTPProtocol(TcpTransport.ClientState state)
        {
            this.state = state;
        }

        public List<byte[]> AddData(Span<byte> bytes)
        {
            throw new InvalidOperationException("HTTPProtocol uses AddRequests.");
        }

        internal List<Request> AddRequestData(Span<byte> bytes)
        {
            var requests = new List<Request>();
            for (int i = 0; i < bytes.Length; i++)
            {
                if (parseState == ParseState.Body)
                {
                    int take = Math.Min(bodyRemaining, bytes.Length - i);
                    body.Write(bytes.Slice(i, take));
                    bodyRemaining -= take;
                    i += take - 1;
                    if (bodyRemaining <= 0) CompleteRequest(requests);
                }
                else ProcessByte(bytes[i], requests);
            }
            return requests;
        }

        void ProcessByte(byte b, List<Request> requests)
        {
            char c = (char)b;
            switch (parseState)
            {
                case ParseState.Method:
                    if (b == ' ') parseState = ParseState.Path;
                    else method.Append(c);
                    break;
                case ParseState.Path:
                    if (b == ' ') parseState = ParseState.Version;
                    else path.Append(c);
                    break;
                case ParseState.Version:
                    if (b == '\r') parseState = ParseState.RequestLineLF;
                    else version.Append(c);
                    break;
                case ParseState.RequestLineLF:
                    if (b != '\n') ResetParser();
                    else parseState = ParseState.HeaderName;
                    break;
                case ParseState.HeaderName:
                    if (b == '\r' && headerName.Length == 0)
                        parseState = ParseState.HeadersEndLF;
                    else if (b == ':')
                        parseState = ParseState.HeaderValueStart;
                    else
                        headerName.Append(c);
                    break;
                case ParseState.HeaderValueStart:
                    if (b == ' ' || b == '\t') break;
                    if (b == '\r')
                    {
                        FinalizeHeader();
                        parseState = ParseState.HeaderLineLF;
                    }
                    else
                    {
                        headerValue.Append(c);
                        parseState = ParseState.HeaderValue;
                    }
                    break;
                case ParseState.HeaderValue:
                    if (b == '\r')
                    {
                        FinalizeHeader();
                        parseState = ParseState.HeaderLineLF;
                    }
                    else headerValue.Append(c);
                    break;
                case ParseState.HeaderLineLF:
                    if (b != '\n') ResetParser();
                    else parseState = ParseState.HeaderName;
                    break;
                case ParseState.HeadersEndLF:
                    if (b != '\n')
                    {
                        ResetParser();
                        break;
                    }
                    bodyRemaining = contentLength;
                    if (bodyRemaining <= 0) CompleteRequest(requests);
                    else parseState = ParseState.Body;
                    break;
                case ParseState.Body:
                    bodyRemaining--;
                    if (bodyRemaining <= 0) CompleteRequest(requests);
                    break;
            }
        }

        void FinalizeHeader()
        {
            string name = headerName.ToString().Trim();
            string value = headerValue.ToString().Trim();
            if (name.Length > 0)
            {
                headers[name] = value;
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(value, out contentLength);
            }
            headerName.Clear();
            headerValue.Clear();
        }

        void CompleteRequest(List<Request> requests)
        {
            var bodyBytes = body.ToArray();
            var request = new Request(method.ToString(), path.ToString(), bodyBytes.Length > 0 ? Encoding.UTF8.GetString(bodyBytes) : "")
            {
                Version = version.ToString(),
                Headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase),
                BodyBytes = bodyBytes
            };
            request.ParseStructuredData();
            requests.Add(request);
            ResetParser();
        }

        void ResetParser()
        {
            parseState = ParseState.Method;
            method.Clear();
            path.Clear();
            version.Clear();
            headerName.Clear();
            headerValue.Clear();
            headers.Clear();
            contentLength = 0;
            bodyRemaining = 0;
            body.SetLength(0);
        }

        internal bool TryUpgradeToWebSocket(int httpRequestId, Request request)
        {
            bool isUpgradeToWS = request.Headers.ContainsKey("Upgrade") && request.Headers["Upgrade"].Equals("websocket", StringComparison.OrdinalIgnoreCase);
            bool supportPerMessageDeflate = request.Headers.ContainsKey("Sec-WebSocket-Extensions") && request.Headers["Sec-WebSocket-Extensions"].Contains("permessage-deflate", StringComparison.OrdinalIgnoreCase);
            string clientKey = request.Headers.ContainsKey("Sec-WebSocket-Key") ? request.Headers["Sec-WebSocket-Key"] : null;
            if (!isUpgradeToWS || clientKey == null) return false;

            const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            string acceptKey;
            using (SHA1 sha1 = SHA1.Create())
                acceptKey = Convert.ToBase64String(sha1.ComputeHash(Encoding.UTF8.GetBytes(clientKey + WebSocketGuid)));

            request.ParseStructuredData();
            state.webSocketPath = request.Path;

            var wsp = new WebSocketProtocol();
            wsp.compressed = supportPerMessageDeflate;
            PublishResponse(
                httpRequestId,
                Encoding.UTF8.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n"),
                false,
                wsp);

            return true;
        }

        internal void PublishResponse(int httpRequestId, byte[] payload, bool closeAfterSend, WebSocketProtocol upgradeProtocol)
        {
            string preview = GetResponsePreview(payload, 32);
            //Console.WriteLine($"REST[{httpRequestId}] #{state.id} --> sending response body=\"{preview}\"");
            if (state.worker == null)
            {
                if (upgradeProtocol != null) state.protocol = upgradeProtocol;
                return;
            }
            state.worker.SendMessageQueue.EnqueueAndNotify(new TcpTransport.SendPacket
            {
                state = state,
                message = payload,
                preFramed = true,
                closeAfterSend = closeAfterSend
            }, state.worker.SendMessageQueue);
            if (upgradeProtocol != null) state.protocol = upgradeProtocol;
        }

        internal static byte[] BuildPayload(Response response, bool keepAlive)
        {
            int byteCount = response.BinaryData != null ? response.BinaryData.Length : Encoding.UTF8.GetByteCount(response.Body);
            var httpResponse = $"HTTP/1.1 {response.StatusCode} {GetStatusMessage(response.StatusCode)}\r\n" +
                               $"Content-Type: {response.ContentType}\r\n" +
                               $"Content-Length: {byteCount}\r\n" +
                               $"Connection: {(keepAlive ? "keep-alive" : "close")}\r\n" +
                               string.Join("", response.ExtraHeaders.Select(header => $"{header.Key}: {header.Value}\r\n")) +
                               "\r\n";

            return response.BinaryData != null
                ? Enumerable.Concat(Encoding.UTF8.GetBytes(httpResponse), response.BinaryData).ToArray()
                : Encoding.UTF8.GetBytes(httpResponse + response.Body);
        }

        static int IndexOfHeaderEnd(byte[] bytes)
        {
            for (int i = 0; i <= bytes.Length - 4; i++)
                if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
                    return i;
            return -1;
        }

        internal static bool WantsKeepAlive(Request request)
        {
            var conn = request.GetHeader("Connection");
            if (conn != null && conn.Equals("close", StringComparison.OrdinalIgnoreCase))
                return false;
            if (conn != null && conn.Equals("keep-alive", StringComparison.OrdinalIgnoreCase))
                return true;
            return !string.Equals(request.Version, "HTTP/1.0", StringComparison.OrdinalIgnoreCase);
        }

        public byte[] CreateMessage(byte[] source)
        {
            return source;
        }

        static string GetResponsePreview(byte[] payload, int maxChars)
        {
            for (int i = 0; i <= payload.Length - 4; i++)
                if (payload[i] == '\r' && payload[i + 1] == '\n' && payload[i + 2] == '\r' && payload[i + 3] == '\n')
                {
                    var body = Encoding.UTF8.GetString(payload, i + 4, payload.Length - (i + 4));
                    body = body.Replace("\r", "\\r").Replace("\n", "\\n");
                    return body.Length > maxChars ? body.Substring(0, maxChars) : body;
                }
            return "";
        }

        static string GetStatusMessage(int statusCode)
        {
            return statusCode switch
            {
                100 => "Continue",
                101 => "Switching Protocols",
                102 => "Processing",
                103 => "Early Hints",
                200 => "OK",
                201 => "Created",
                202 => "Accepted",
                203 => "Non-Authoritative Information",
                204 => "No Content",
                205 => "Reset Content",
                206 => "Partial Content",
                207 => "Multi-Status",
                208 => "Already Reported",
                226 => "IM Used",
                300 => "Multiple Choices",
                301 => "Moved Permanently",
                302 => "Found",
                303 => "See Other",
                304 => "Not Modified",
                305 => "Use Proxy",
                306 => "Switch Proxy",
                307 => "Temporary Redirect",
                308 => "Permanent Redirect",
                400 => "Bad Request",
                401 => "Unauthorized",
                402 => "Payment Required",
                403 => "Forbidden",
                404 => "Not Found",
                405 => "Method Not Allowed",
                406 => "Not Acceptable",
                407 => "Proxy Authentication Required",
                408 => "Request Timeout",
                409 => "Conflict",
                410 => "Gone",
                411 => "Length Required",
                412 => "Precondition Failed",
                413 => "Payload Too Large",
                414 => "URI Too Long",
                415 => "Unsupported Media Type",
                416 => "Range Not Satisfiable",
                417 => "Expectation Failed",
                418 => "I'm a teapot",
                421 => "Misdirected Request",
                422 => "Unprocessable Content",
                423 => "Locked",
                424 => "Failed Dependency",
                425 => "Too Early",
                426 => "Upgrade Required",
                428 => "Precondition Required",
                429 => "Too Many Requests",
                431 => "Request Header Fields Too Large",
                451 => "Unavailable For Legal Reasons",
                500 => "Internal Server Error",
                501 => "Not Implemented",
                502 => "Bad Gateway",
                503 => "Service Unavailable",
                504 => "Gateway Timeout",
                505 => "HTTP Version Not Supported",
                506 => "Variant Also Negotiates",
                507 => "Insufficient Storage",
                508 => "Loop Detected",
                510 => "Not Extended",
                511 => "Network Authentication Required",
                _ => "Unknown"
            };
        }
    }
}
