#if USE_AUTH_JWT
using Auth;
using PiGSF.Server;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;


// THIS IS NOT SECURE, JUST PLAYING!
public class JWTAuth: IAuthProvider
{
    private static readonly string SecretKey = ServerConfig.JWTPrivateKey;

    // Generate a JWT
    public static string GenerateJwt(Dictionary<string, string> payload)
    {
        var header = Base64UrlEncode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payloadJson = JsonConvert.SerializeObject(payload);
        var payloadEncoded = Base64UrlEncode(payloadJson);

        var signature = ComputeSignature($"{header}.{payloadEncoded}");
        return $"{header}.{payloadEncoded}.{signature}";
    }

    // Validate a JWT
    public static bool ValidateJwt(string token, out Dictionary<string, string>? payload)
    {
        payload = null;

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false; // Invalid token structure
        }

        var header = parts[0];
        var payloadEncoded = parts[1];
        var signature = parts[2];

        var expectedSignature = ComputeSignature($"{header}.{payloadEncoded}");
        if (signature != expectedSignature)
        {
            return false; // Signature mismatch
        }

        // Decode and deserialize payload
        var payloadJson = Base64UrlDecode(payloadEncoded);
        payload = JsonConvert.DeserializeObject<Dictionary<string, string>>(payloadJson);
        return true;
    }

    private static string ComputeSignature(string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SecretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(string input)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(input))
            .Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    private static string Base64UrlDecode(string input)
    {
        var output = input.Replace("-", "+").Replace("_", "/");
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Encoding.UTF8.GetString(Convert.FromBase64String(output));
    }

    public async Task<PlayerData> Authenticate(string inputData)
    {
        if (ValidateJwt(inputData, out var payload))
        {
            payload.TryGetValue("Name", out var name);
            payload.TryGetValue("Username", out var username);
            payload.TryGetValue("Uid", out var uid);
            return new PlayerData { name= name, username= username, uid=uid };
        }
        return null;
    }
}
#endif