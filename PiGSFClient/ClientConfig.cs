using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Client
{
    public static class ClientConfig
    {
        public static string serverAddress = "chessocracy.com";
        //public static string serverAddress = "127.0.0.1";
        public static int serverPort = 8443;
        public static int numberOfTests = 2;

        // Server config
        public static int HeaderSize = 2;

        public static RSA publicKey = null;

        static ClientConfig()
        {
            //var publicKeyData = File.ReadAllText("server_public_key.pem");
            var publicKeyData = """
                -----BEGIN PUBLIC KEY-----
                MFwwDQYJKoZIhvcNAQEBBQADSwAwSAJBANQ4jp00tczBJalIJZx91S6PjXp5Q2AS
                21aiplVICND1e+4zkPj2dVQLxBH1wttsLIU6HhvfbCnRdTbKCcn1ly0CAwEAAQ==
                -----END PUBLIC KEY-----
                """;
            var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyData);
            
        }
    }
}
