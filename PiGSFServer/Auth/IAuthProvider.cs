using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Auth
{
    [Serializable]
    public partial class PlayerData
    {
        public string name { get; set; } = "Guest";
        public string username { get; set; } = "guest";
        public string uid { get; set; } = "anon:guest";
        public string avatar { get; set; } = "";
    }

    public interface IAuthProvider
    {
        Task<PlayerData> Authenticate(string inputData);
    }
}
