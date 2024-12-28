using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Auth
{
    public class NoAuth : IAuthProvider
    {
        public async Task<PlayerData> Authenticate(string inputData)
        {
            return new PlayerData { name = inputData, username = inputData, uid = "anon:" + new Guid() };
        }
    }

    // NEVER use this unless testing - it will link to existing player by mere username
    public class NoAuthStable : IAuthProvider
    {
        public async Task<PlayerData> Authenticate(string inputData)
        {
            return new PlayerData { name = inputData, username = inputData, uid = "anon:" + inputData };
        }
    }
}
