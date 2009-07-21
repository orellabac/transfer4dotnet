using GZNU.Interfaces;

namespace GZNU.NetworkServer
{
    /// <summary>
    /// 接口，收发数据的实体。
    /// </summary>
    public interface INode
    {
        /// <summary>
        /// 收到数据时会产生该事件
        /// </summary>
        event DataReceivedHandler DataReceived;

        /// <summary>
        /// 连接成功后会产生该事件
        /// </summary>
        event ConnectedHandler Connected;

        /// <summary>
        /// 连接已经断开事件
        /// </summary>
        event DisconnectedHandler Disconnected;

        /// <summary>
        /// 开启连接
        /// </summary>
        /// <returns>是否成功</returns>
        bool Connect();

        /// <summary>
        /// 是否已经开始服务
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 断开连接
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 向对方发送数据
        /// </summary>
        /// <param name="data">发送的数据,必须实现IData接口,并且标记为[Serializable]</param>
        /// <returns>发送成功返回true,否则false.</returns>
        bool SendData(IData data);

        /// <summary>
        /// 向指定的节点(已经与该节点建立连接)发送数据
        /// </summary>
        /// <param name="data">发送的数据,必须实现IData接口,并且标记为[Serializable]</param>
        /// <param name="address">IP地址</param>
        /// <param name="port">端口</param>
        /// <returns>发送成功返回true,否则false.</returns>
        bool SendData(IData data, string address, int port);

        /// <summary>
        /// 将数据发送到所有与之相连的客户
        /// </summary>
        /// <param name="data">发送的数据,必须实现IData接口,并且标记为[Serializable]</param>
        /// <returns>发送成功返回true,否则false.</returns>
        bool BroadcastData(IData data);
    }
}
