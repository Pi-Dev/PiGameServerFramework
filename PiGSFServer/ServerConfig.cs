using Auth;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

// Extra things to add to the Authenticator PlayerData object
namespace Auth
{
    public partial class PlayerData
    {
        public long gsid;
        public class GoogleAuth
        {
            public string accessToken;
            public string refreshToken;
            public long accessExp;
            public string sub;
            public string email;
            public string avatarUrl;
            public string displayName;
        }
        public GoogleAuth google;
    }
}

// Add extra things to the Server Player object
namespace PiGSF.Server
{
    public partial class Player
    {
        // These are used by the examples, but you are free to use them as you like
        public string team = "default";
        public string name = "Guest";
        public string username = "guest";
        public string avatarUrl = "";

        // MMR test
        public double MMR = 1000;

        public JObject? ToJson()
        {
            //if (playerData == null) return null;
            var jo = new JObject();
            jo["gsid"] = playerData?.gsid ?? 0;
            jo["name"] = name;
            jo["username"] = username;
            jo["avatar"] = avatarUrl;
            jo["MMR"] = MMR;
            if (playerData != null && playerData.google != null) jo["google"] = JObject.FromObject(playerData.google);
            return jo;
        }

        public override string? ToString() => $"[{id}] {name} = {uid}";

        public int GetRating(string category)
        {
            return 0; // Get rank for given category
        }
    }
}

public static class ServerConfig
{
    static ServerConfig()
    {
        // Default config, hard-coded, and very limited.
        // You are supposed to build the server and implement your game types
        var defaultConfig = new Dictionary<string, string>() {
                { "DefaultRoomConnectionTimeout", "30" },
                { "bindAddress", "0.0.0.0" },
                { "bindPort", "12345" },
                { "defaultRoom", "ChatRoom,Lobby" },
                { "TCPClientsPerWorker", "128" },
                { "SSLServerCertPem", "~/ServerCert.pem" },
            };

        // parse config file
        var configFile = "PIGSFServerConfig.cfg";
        var fp = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/" + configFile;
        try
        {
            var config = File.ReadAllLines(fp)
                .Where(l => !l.StartsWith("#") && l.Contains("="))
                .Select(s => s.Split('='))
                .ToDictionary(x => x[0].Trim(), x => x[1].Trim());
            foreach (var c in config) defaultConfig[c.Key] = c.Value;
        }
        catch (Exception ex)
        {
            ServerLogger.Log(ex.Message);
        }

        // Apply to the class
        ServerConfig.config = new ReadOnlyDictionary<string, string>(defaultConfig);
    }

    // Packet size and format
    public static int PolledBuffersSize = 1024; // by default 1k
    public static int MaxInitialPacketSize = 4 * 1024; // by default 4k for JWT payload

    // Implementation details
    static ReadOnlyDictionary<string, string> config;
    public static string Get(string key, string defval = "") => config.GetValueOrDefault(key, defval);
    public static int GetInt(string key, int defval = 0)
    {
        var str = config.GetValueOrDefault(key, defval.ToString());
        if (int.TryParse(str, out int val)) return val;
        return defval;
    }

    // Room configuration

    // Time to keep room if no players reconnect
    public static int KeepRoomIfNoPlayersReconnect = 300;
    // Time before disconnecting player who did not sent message
    public static int DefaultRoomConnectionTimeout = 300;

    // Authentication modules by default
    public static IAuthProvider[] authProviders = { /*new JWTAuth(),*/ new NoAuth() };

    public static string EncryptionPublicKey => LoadFileOrDefault("PIGSF-PUBLIC-RSA.PEM");
    public static string EncryptionPrivateKey => LoadFileOrDefault("PIGSF-PRIVATE-RSA.PEM");

    public static string LoadFileOrDefault(string fn, string def = "")
    {
        try { return File.ReadAllText(fn); }
        catch { return def; }
    }
}
