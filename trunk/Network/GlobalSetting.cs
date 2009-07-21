
namespace GZNU.NetworkServer
{
    class GlobalSetting
    {
        /// <summary>
        /// 发送链路状态检验数据的间隔ms
        /// </summary>
        public static int HeartBeatInterval = 10000;

        /// <summary>
        /// 侦听客户端的时间间隔
        /// </summary>
        public static int ListenInterval = 5000;

        /// <summary>
        /// 检查是否有客户端数据到达时间间隔,默认为10ms
        /// </summary>
        public static int CheckDataInterval = 10;

        /// <summary>
        /// 能接受的最大挂起连接数
        /// </summary>
         public static  int MaxPendingConnections = 50;
    }
}
