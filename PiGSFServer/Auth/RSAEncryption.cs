using System;
using System.Security.Cryptography;
using System.Text;

namespace Auth
{
    public static class RSAEncryption
    {
        public static (string PrivateKey, string PublicKey) GenerateRSAKeyPairs(int bits)
        {
            using (var rsa = RSA.Create(bits))
            {
                // Export keys as byte arrays
                byte[] privateKeyBytes = rsa.ExportRSAPrivateKey();
                byte[] publicKeyBytes = rsa.ExportRSAPublicKey();

                // Convert to Base64 PEM format
                string privateKeyPem = $"-----BEGIN PRIVATE KEY-----\n{Convert.ToBase64String(privateKeyBytes, Base64FormattingOptions.InsertLineBreaks)}\n-----END PRIVATE KEY-----";
                string publicKeyPem = $"-----BEGIN PUBLIC KEY-----\n{Convert.ToBase64String(publicKeyBytes, Base64FormattingOptions.InsertLineBreaks)}\n-----END PUBLIC KEY-----";

                return (privateKeyPem, publicKeyPem);
            }
        }
    }
}