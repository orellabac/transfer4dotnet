
namespace GZNU.NetworkServer
{
    /// <summary>
    /// 收到数据处理例程,用于DataReceived事件。
    /// </summary>
    /// <param name="sender">导致事件发生的对象</param>
    /// <param name="e">事件的参数</param>
    public delegate void DataReceivedHandler(object sender, ReceivedEventArgs e);

    /// <summary>
    /// 连接建立后的处理例程,用于Connected事件。
    /// </summary>
    /// <param name="sender">导致事件发生的对象</param>
    /// <param name="e">事件的参数</param>
    public delegate void ConnectedHandler(object sender, ConnectedEventArgs e);

    /// <summary>
    /// 连接断开后的处理全程，用于Disconnected事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public delegate void DisconnectedHandler(object sender, ConnectedEventArgs e);
}
