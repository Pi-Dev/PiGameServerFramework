using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Client.Transport
{
    public interface ITransport: IDisposable
    {
        void Connect(string address, int port);
        void SendBytes(byte[] data);
        void SendString(string data);
        void Close();
        bool IsConnected();
    }
}
