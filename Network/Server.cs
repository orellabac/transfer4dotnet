using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections;
using GZNU.Interfaces;
using SimmoTech.Utils.Compression;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

/*
  提供了一系列C/S架构的基础类
  在该类中提供了基于事件的数据收发功能
 */
/*
 *This project used ManagedLZO.MiniLZO to compress and decompress.
 *Thanks for Markus Franz Xaver Johannes Oberhumer and Shane Eric Bryldt.
 *You can get it on http://www.codeproject.com/KB/recipes/managedlzo.aspx.
 */

namespace GZNU.NetworkServer
{
	/// <summary>
	/// 服务端
	/// </summary>
	public class Server : IDisposable, INode
	{
		/// <summary>
		/// 连接至服务端的每个客户端。
		/// </summary>
		internal class Client
		{
			public Socket clientSocket;
			//////下面两个时间间隔不应当大于2倍的keepAliveInterval
			public DateTime clientHearbeatTime;//客户端最新的心跳时间－－收到时间
			public DateTime serverHearbeatTime;//服务端最新的心跳时间--发出时间

			public Client(Socket clientSocket, DateTime clientHeartbeatTime, DateTime serverHeatbeatTime)
			{
				this.clientSocket = clientSocket;
				this.clientHearbeatTime = clientHeartbeatTime;
				this.serverHearbeatTime = serverHeatbeatTime;
			}
		}

		#region private member
		private object lockForClients = new object();//用于clients的同步更改

		private int listenInterval = GlobalSetting.ListenInterval;  // 侦听客户端的时间间隔，默认为100ms
		private int maxConnections = GlobalSetting.MaxPendingConnections;//能接受的最大挂起连接数
		private int keepAliveInterval = GlobalSetting.HeartBeatInterval;//发送心跳时间间隔。应当全局设置

		private Hashtable IP2Index = new Hashtable();// 客户端--index列表
		private ArrayList clients = new ArrayList();
		private int port = 9501;// 默认端口号
		private bool isConnected = false;//服务端是否开始侦听

		private Thread threadListen;//数据侦听线程
		private Thread threadHeartbeat;//心跳线程
		private Thread threadDataReceive;//数据接收

		private Heartbeat heartbeat = new Heartbeat();
		#endregion

		#region public property
		/// <summary>
		/// 侦听客户端的时间间隔，默认为500ms
		/// </summary>
		public int ListenInterval
		{
			get { return listenInterval; }
			set { listenInterval = value; }
		}

		/// <summary>
		/// 查询服务是否开始
		/// </summary>
		public bool IsConnected
		{
			get { return isConnected; }
		}

		#endregion

		#region private method

		/// <summary>
		/// 引发Connected事件
		/// </summary>
		/// <param name="e"></param>
		private void OnConnected(object e)
		{
			try
			{
				if (Connected != null)
					Connected(this, (ConnectedEventArgs)e);
			}
			catch
			{
				throw ;
			}
		}

		/// <summary>
		/// 引发Disconnected事件
		/// </summary>
		/// <param name="e"></param>
		private void OnDisconnected(object e)
		{
			try
			{
				if (Disconnected != null)
					Disconnected(this, (ConnectedEventArgs)e);
			}
			catch
			{
				throw;
			}
		}

		/// <summary>
		/// 引发DataReceived事件
		/// </summary>
		/// <param name="e"></param>
		private void OnDataReceived(object e)
		{
			try
			{
				if (DataReceived != null)
					DataReceived(this, (ReceivedEventArgs)e);
			}
			catch
			{
				throw;
			}
		}


		/// <summary>
		/// 侦听接入的每个连接
		/// </summary>
		private void listenClient()
		{
			while (isConnected)
			{
				try
				{
					while (isConnected && !Serversocket.Pending())
						System.Threading.Thread.Sleep(this.listenInterval);
					if (!isConnected) break;//服务器停了

					//有连接时
					Socket newClientSocket = Serversocket.AcceptSocket();
					newClientSocket.SendTimeout = 10000;
					newClientSocket.ReceiveTimeout = 10000;

					Client newClient = new Client(newClientSocket, DateTime.Now, DateTime.Now);
					lock (lockForClients)
					{
						clients.Add(newClient);
						IP2Index.Add(newClientSocket.RemoteEndPoint, clients.IndexOf(newClient));
					}

					if (Connected != null)//fire the Client Connected event
					{
						try
						{
							ConnectedEventArgs e = new ConnectedEventArgs();
							e.RemoteEndPoint = (IPEndPoint)(newClientSocket.RemoteEndPoint);
							ThreadPool.QueueUserWorkItem(new WaitCallback(OnConnected), e);
						}
						catch (Exception err)
						{
							System.Diagnostics.Debug.WriteLine("Server: exception for client connected event.\n" + err.ToString());
						}
					}
				}
				catch (Exception error)
				{
					System.Diagnostics.Debug.WriteLine("Error: in Server.ListenClient()." + error.ToString());
				}
			}
		}

		/// <summary>
		/// 检查连接的客户端是否断连,处理客户端列表
		/// </summary>
		private void keepAlive()
		{
			int n = 0;
			TimeSpan interval;
			//System.Threading.Thread.Sleep(10000);//使客户端有足够的时间将状态转换至isConnected=true.
			ArrayList disconnectedClients = new ArrayList();
			int heartbeatNo = 0;
			while (isConnected)
			{
				n = (n == 0) ? 1 : 0;
				//send
				heartbeatNo = (heartbeatNo + 1) % int.MaxValue;
				heartbeat.No = heartbeatNo;
				SendHeartbeat(heartbeat);

				System.Threading.Thread.Sleep(GlobalSetting.HeartBeatInterval);

				if (n == 0) //是检验是否受到Heartbeat的时候了
				{
					lock (lockForClients)
					{
						try
						{
							foreach (Client c in clients)
							{
								interval = (c.serverHearbeatTime > c.clientHearbeatTime) ? (c.serverHearbeatTime - c.clientHearbeatTime) : (c.clientHearbeatTime - c.serverHearbeatTime);
								if (interval.TotalMilliseconds > 2 * GlobalSetting.HeartBeatInterval)//disconnected
								{
									disconnectedClients.Add(c);
								}
							}

							if (disconnectedClients.Count != 0)
							{
								foreach (Client c in disconnectedClients)
								{
									clients.Remove(c);
								}
								//reindex
								IP2Index.Clear();
								foreach (Client c in clients)
								{
									IP2Index.Add((IPEndPoint)c.clientSocket.RemoteEndPoint, clients.IndexOf(c));
								}
							}
						}
						catch
						{
						}
					}
					//handle disconnected
					if (disconnectedClients.Count != 0)
					{
						ConnectedEventArgs e = new ConnectedEventArgs();
						foreach (Client c in disconnectedClients)
						{
							try
							{
								e.RemoteEndPoint = (IPEndPoint)c.clientSocket.RemoteEndPoint;
								ThreadPool.QueueUserWorkItem(new WaitCallback(OnDisconnected), e);
							}
							catch
							{
							}
						}
						disconnectedClients.Clear();
					}
				}
			}
		}

		private void ReceiveData()
		{
			byte[] dataL = new byte[sizeof(int)];
			int length = 0;
			bool err = false;

			while (isConnected)
			{
				Thread.Sleep(GlobalSetting.CheckDataInterval);
				try
				{
					lock (lockForClients)
					{
						for (int i = 0; i < clients.Count; i++)
						{
							//从服务器有数据可读?
							if (((Client)clients[i]).clientSocket.Available == 0) continue;

							//首先获得数据的长度
							if (GetData(((Client)clients[i]).clientSocket, ref dataL, sizeof(int)) != sizeof(int))//数据 不OK
							{
								err = true;
								continue;
							}
							//数据OK
							length = BitConverter.ToInt32(dataL, 0);
							if (length <= 0)//数据长度是<= 0
							{
								err = true;
								continue;
							}

							byte[] data = new byte[length];
							//获取整个数据
							if (GetData(((Client)clients[i]).clientSocket, ref data, length) == length)
							{
								try
								{
									ReceivedEventArgs eArgs = new ReceivedEventArgs();
									eArgs.RemoteEndPoint = (IPEndPoint)((Client)clients[i]).clientSocket.RemoteEndPoint;
									MemoryStream ms = new MemoryStream(MiniLZO.Decompress(data));
									BinaryFormatter bfm = new BinaryFormatter();
									eArgs.Data = (IData)bfm.Deserialize(ms);
									ms.Close();
									
									if (eArgs.Data.Type == heartbeat.Type)//is heartbeat?
									{
										//更新客户端的最新心跳包时间
										((Client)clients[i]).clientHearbeatTime = DateTime.Now;
									}
									else//no, it is user's data.
									{
										//引发事件
										if (DataReceived != null)
											ThreadPool.QueueUserWorkItem(new WaitCallback(OnDataReceived), eArgs);
									}
								}
								catch
								{
									System.Diagnostics.Debug.WriteLine("Error: in client. resolve data or DataReceived event.");
									throw;
								}

							}
							else//something is error.
							{
								err = true;
							}
						}
					}
				}
				catch (Exception error)
				{
					System.Diagnostics.Debug.WriteLine("Error in Client.Checkdata:\n" + error.ToString());
					err = true;
				}
				//GC.Collect();
			}
		}

		private int GetData(Socket clientSocket, ref byte[] data, int size)
		{
			if (data.Length < size) size = data.Length;
			int total = 0;
			int dataleft = size;
			int recv = 0;
			try
			{
				while (total < size)
				{
					if (!isConnected) break;
					recv = 0;
					try
					{
						Monitor.Enter(clientSocket);
						//if (clientSocket.Available > 0)
						recv = clientSocket.Receive(data, total, dataleft, SocketFlags.None);
						if (!clientSocket.Connected) throw new Exception();
					}
					catch (Exception err)
					{
						System.Diagnostics.Debug.WriteLine(err.ToString());
						break;
					}
					finally
					{
						Monitor.Exit(clientSocket);
					}
					if (recv == 0)
					{
						System.Threading.Thread.Sleep(1000);
						continue;
					}

					total += recv;
					dataleft -= recv;
				}
			}
			catch (SocketException err)
			{
				System.Diagnostics.Debug.WriteLine("Error: in Client.receiveData().");
				System.Diagnostics.Debug.WriteLine(err.ToString());
			}
			return total;
		}

		/// <summary>
		/// 向所有的客户端发送心跳包
		/// </summary>
		/// <param name="heartbeat"></param>
		/// <returns>是否成功？</returns>
		private bool SendHeartbeat(Heartbeat heartbeat)
		{
			if (!isConnected) return false;
			byte[] alldata;
			try
			{
				//serialize
				BinaryFormatter bfm = new BinaryFormatter();
				MemoryStream ms = new MemoryStream();
				bfm.Serialize(ms, heartbeat);

				//compress
				byte[] comData = MiniLZO.Compress(ms);
				ms.Close();

				alldata = new byte[sizeof(int) + comData.Length];

				Buffer.BlockCopy(BitConverter.GetBytes(comData.Length), 0, alldata, 0, sizeof(int));
				Buffer.BlockCopy(comData, 0, alldata, sizeof(int), comData.Length);
			}
			catch (Exception err)
			{
				System.Diagnostics.Debug.WriteLine(err.ToString());
				return false;
			}

			try
			{
				lock (lockForClients)
				{
					for (int i = 0; i < clients.Count; i++)
					{

						try
						{
							((Client)clients[i]).serverHearbeatTime = DateTime.Now;
							lock (((Client)clients[i]).clientSocket)
							{
								((Client)clients[i]).clientSocket.NoDelay = true;
								((Client)clients[i]).clientSocket.Send(alldata);
							}
						}
						catch
						{
							((Client)clients[i]).clientSocket.NoDelay = false;
						}
					}
				}
				return true;
			}
			catch (Exception err)
			{
				System.Diagnostics.Debug.WriteLine("Error in Client.SendData()\n"+err.ToString ());
				return false;
			}
		}

		#endregion

		#region public property

		/// <summary>
		/// 服务器端口
		/// </summary>
		public int Port
		{
			set { port = value; }
			get { return port; }
		}

		/// <summary>
		/// 最大接收的挂起连接数，默认为50个
		/// </summary>
		public int MaxConnections
		{
			get { return maxConnections; }
			set { maxConnections = value; }
		}

		#endregion

		#region public member

		/// <summary>
		/// 用于侦听的socket
		/// </summary>
		public TcpListener Serversocket;

		#endregion

		#region public Events

		/// <summary>
		/// 数据收到事件,收到客户的数据时会产生该事件
		/// </summary>
		public event DataReceivedHandler DataReceived;

		/// <summary>
		/// 客户端连接已经建立事件
		/// </summary>
		public event ConnectedHandler Connected;

		/// <summary>
		/// 客户端连接已经断开事件
		/// </summary>
		public event DisconnectedHandler Disconnected;

		#endregion

		#region public method

		/// <summary>
		/// 构造函数
		/// </summary>
		public Server()
		{
			clients = ArrayList.Synchronized(clients);
		}

		/// <summary>
		/// 开启服务
		/// </summary>
		public bool Connect()
		{
			if (isConnected) return true;
			try
			{
				if (Serversocket != null)
				{
					try
					{
						Serversocket.Stop();
						Serversocket = null;
					}
					catch
					{
					}
				}
				Serversocket = new TcpListener(IPAddress.Any, port);
				Serversocket.Start(maxConnections);
				isConnected = true;

				//应该用一个线程来完成连接的侦听。
				threadListen = new Thread(new ThreadStart(listenClient));
				threadListen.IsBackground = true;
				threadListen.Start();
				threadHeartbeat = new Thread(new ThreadStart(keepAlive));
				threadHeartbeat.IsBackground = true;
				threadHeartbeat.Start();
				threadDataReceive = new Thread(new ThreadStart(ReceiveData));
				threadDataReceive.IsBackground = true;
				threadDataReceive.Start();

				return true;
			}
			catch
			{
				if (Serversocket != null)
				{
					Serversocket.Stop(); Serversocket = null;
				}
				System.Diagnostics.Debug.WriteLine("Error:Start Server failed.");
				return false;
			}
		}

		/// <summary>
		/// 停止服务
		/// </summary>
		public void Disconnect()
		{
			if (!isConnected) return;
			try
			{
				isConnected = false;
				System.Threading.Thread.Sleep(1000);
				threadListen.Abort();
				threadDataReceive.Abort();
				threadHeartbeat.Abort();
				lock (lockForClients)
				{
					foreach (Client client in clients)
					{
						lock (client.clientSocket)
						{
							client.clientSocket.Shutdown(SocketShutdown.Both);
							client.clientSocket.Close();
						}
					}
				}
				if (Serversocket != null)
				{
					Serversocket.Stop();
					Serversocket = null;
				}
			}
			catch (Exception err)
			{
				System.Diagnostics.Debug.WriteLine("Error:Server fail to stop.");
				System.Diagnostics.Debug.WriteLine(err.ToString());
			}
		}

		/// <summary>
		/// 向每个客户端发送数据
		/// </summary>
		/// <param name="data">发送的数据,必须实现IData接口,并且标记为[Serializable]</param>
		/// <returns>是否成功</returns>
		public bool BroadcastData(IData data)
		{
			if (!isConnected) return false;
			byte[] alldata;
			try
			{
				//serialize
				BinaryFormatter bfm = new BinaryFormatter();
				MemoryStream ms = new MemoryStream();
				bfm.Serialize(ms, data);

				//compress
				byte[] comData = MiniLZO.Compress(ms);
				ms.Close();

				alldata = new byte[sizeof(int) + comData.Length];

				Buffer.BlockCopy(BitConverter.GetBytes(comData.Length), 0, alldata, 0, sizeof(int));
				Buffer.BlockCopy(comData, 0, alldata, sizeof(int), comData.Length);
			}
			catch (System.Runtime.Serialization.SerializationException)
			{
				throw;
			}
			catch (Exception err)
			{
				System.Diagnostics.Debug.WriteLine(err.ToString());
				return false;
			}
			bool r = true;
			lock (lockForClients)
			{
				foreach (Client client in clients)
				{
					try
					{
						client.clientSocket.Send(alldata);
					}
					catch (Exception err)
					{
						System.Diagnostics.Debug.WriteLine("Error: in Server.BroadcastData");
						System.Diagnostics.Debug.WriteLine(err.ToString());
						r = false;
					}
				}
			}
			return r;
		}

		/// <summary>
		/// 向每个客户端发送数据
		/// </summary>
		/// <param name="data">发送的数据,必须实现IData接口,并且标记为[Serializable]</param>
		/// <returns>是否成功</returns>
		public bool SendData(IData data)
		{
			return BroadcastData(data);
		}

		/// <summary>
		/// 向指定的节点发送数据
		/// </summary>
		/// <param name="data">发送的数据,必须实现IData接口,并且标记为[Serializable]</param>
		/// <param name="address">IP地址</param>
		/// <param name="port">端口</param>
		/// <returns>是否成功</returns>
		public bool SendData(IData data, string address, int port)
		{
			if (!isConnected) return false;
			byte[] alldata;
			try
			{
				//serialize
				BinaryFormatter bfm = new BinaryFormatter();
				MemoryStream ms = new MemoryStream();
				bfm.Serialize(ms, data);

				//compress
				byte[] comData = MiniLZO.Compress(ms);
				ms.Close();

				alldata = new byte[sizeof(int) + comData.Length];

				Buffer.BlockCopy(BitConverter.GetBytes(comData.Length), 0, alldata, 0, sizeof(int));
				Buffer.BlockCopy(comData, 0, alldata, sizeof(int), comData.Length);
			}
			catch (System.Runtime.Serialization.SerializationException)
			{
				throw;
			}
			catch (Exception err)
			{
				System.Diagnostics.Debug.WriteLine(err.ToString());
				return false;
			}

			bool r = false;
			lock (lockForClients)
			{
				try
				{
					IPEndPoint dst = new IPEndPoint(IPAddress.Parse(address), port);
					((Client)(clients[(int)IP2Index[dst]])).clientSocket.Send(alldata);
				}
				catch
				{
				}
			}
			return r;
		}

		#endregion

		#region IDisposable 成员
		private bool isDisposed = false;
		/// <summary>
		/// Dispose
		/// </summary>
		/// <param name="disposing"></param>
		protected virtual void Dispose(bool disposing)
		{
			if (!isDisposed)
			{
				if (disposing)
				{
				}
				Disconnect();
			}
			isDisposed = true;
		}
		/// <summary>
		/// 析构函数
		/// </summary>
		~Server()
		{
			Disconnect();
		}
		/// <summary>
		/// 注销Server，释放资源
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		#endregion
	}
}
