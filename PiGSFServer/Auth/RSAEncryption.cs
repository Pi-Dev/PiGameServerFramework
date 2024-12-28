using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Auth
{
    public static class RSAEncryption
    {
        public static (string PrivateKey, string PublicKey) GenerateRSAKeyPairs(int bits)
        {
            using (var rsa = RSA.Create(bits))
            {
                string pub = rsa.ExportSubjectPublicKeyInfoPem();
                string priv = rsa.ExportPkcs8PrivateKeyPem();
                return (priv, pub);
            }
        }
    }
}
