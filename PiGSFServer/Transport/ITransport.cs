﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Server
{
    public interface ITransport {
        public void Init(int port);
        public void Stop();
        public void StopAccepting();
    }
}
