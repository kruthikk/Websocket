using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GainFrameWork.Communication.WebSockets
{
    public interface IServiceFactory
    {
        IService CreateInstance(ConnectionDetails connectionDetails, bool isLowLevel=false);
    }
}
