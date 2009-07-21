 This project used ManagedLZO.MiniLZO to compress and decompress.
 Thanks for Markus Franz Xaver Johannes Oberhumer and Shane Eric Bryldt.
 You can get it on http://www.codeproject.com/KB/recipes/managedlzo.aspx.

Network V1.0组件
作者：Yu Xuhong
Email:yuxuhong@yeah.net

使用说明：

 Server:
     名称：数据服务器
     功能：做为数据服务器，能接收多个客户端的连接
     默认端口号是9501，最多接收的挂起连接数是50
     用法：
     	指定事件DataReceived的实现，如
     		Server server1=new Server();
     		server1.DataRecieved += new DataRecievedHandler (OnRe);
    		protected virtual void OnRe(object sender, ReceivedEventArgs e)
    		{
    			sender为收到事件的对象
     			e.Data为收到的数据，是实现了IData的类的实例,可根据e.Typek识别对应的类型.
     			e.RemoteEndPoint是发送数据方的地址
     			可利用这些参数在事件处理例程中处理收到的数据，同时，也能利用SendData()来向对方发送数据。
    		}
     
     	指定事件Connected的实现，如
     		server1.Connected += new ConnectedHandler (OnCe);
    		protected virtual void OnCe(object sender, ConnectedEventArgs e)
    		{
    			其中，sender为收到事件的对象
     		    e.RemoteEndPoint是连接方的地址
    		}
     
     	设置侦听时间间隔：server1.listenInterval,时间以毫秒为单位。
     	设置轮询客户端数据的时间间隔：server1.checkDataInterval,以毫秒为单位
     	调用Conntect()开始服务端
     	当需要停止服务端时，调用Disconnect()。

 Client:
 	 名称：数据客户端
     功能：做为数据客户，一个客户端只能连接到一个服务端
     用法：
     	指定事件DataReceived的实现，如
     		Client client1=new Client();
     		client1.DataRecieved += new DataRecievedHandler (OnRe);
    		protected virtual void OnRe(object sender, ReceivedEventArgs e)
    		{
    			sender为收到事件的对象
     			e.Data为收到的数据，是实现了IData的类的实例,可根据e.Type识别其类型.
     			e.RemoteEndPoint是发送数据方的地址
     			可利用这些参数在事件处理例程中处理收到的数据，同时，也能利用SendData()来向对方发送数据。
    		}
     
     	指定事件Connected的实现，如
     		client1.Connected += new ConnectedHandler (OnCe);
    		protected virtual void OnCe(object sender, ConnectedEventArgs e)
    		{
    			其中，sender为收到事件的对象
     		    e.RemoteEndPoint是连接方的地址
    		}
     
     	设置轮询数据是否到达的时间间隔：client1.checkDataInterval,以毫秒为单位
     	调用Conntect()开始服务端
     	当需要停止服务端时，调用Disconnect()。

关于要传送的数据：(关于序列化请参考：http://blog.csdn.net/Comman1999/archive/2008/05/28/2489547.aspx）
	待传送的数据必须满足三个条件：
		a.实现IData接口,并且Type的值不能是Int.MinValue,因为已经为心跳数据所用。
		b.具有序列化构造函数（可以被的序列化）
		c.标记为[Serializable],并确实可被序列化
		
	当传送的类较大时，若默认的序列化效率较低，请尝试下面的方法：
		a.实现System.Runtime.Serialization.ISerializable接口，并
		b.使用SimmoTech.Utils.Serialization命名空间提供的工具。
		
	由于在网络传送时，Server与Client已经对序列化数据进行了压缩，故序列化时没有必要对数据进行压缩处理。
	
例子见工程Test。