using System;
using GZNU.Interfaces;

namespace GZNU.NetworkServer
{
    /// <summary>
    /// 事件DataReceived的参数
    /// </summary>
    public class ReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// 发出数据方的IP地址,端口号
        /// </summary>
        public System.Net.IPEndPoint RemoteEndPoint;

        /// <summary>
        /// 所包含的数据
        /// </summary>
        public IData Data;
    }

    /// <summary>
    /// 事件Connected的参数
    /// </summary>
    public class ConnectedEventArgs : EventArgs
    {
        /// <summary>
        /// 对方的IP地址,端口号
        /// </summary>
        public System.Net.IPEndPoint RemoteEndPoint;
    }

}
