using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SimmoTech.Utils.Compression;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using GZNU.Interfaces;

/*
 *This project used ManagedLZO.MiniLZO to compress and decompress.
 *Thanks for Markus Franz Xaver Johannes Oberhumer and Shane Eric Bryldt.
 *You can get it on http://www.codeproject.com/KB/recipes/managedlzo.aspx.
 */

namespace GZNU.NetworkServer
{
	/// <summary>
	/// 名称：客户端类
	/// 功能：从数据服务器接收、发送数据
	/// </summary>
	public class Client : IDisposable, INode
	{
		#region private member

		private bool isConnecting = false;// 防止重新连接
		private string serverAddress;//服务端IP
		private int serverPort;//服务端的端口
		private Socket serverSocket;
		private bool isConnected = false;
		private Thread threadCheckRecieved;
		private Thread threadCheckActive;
		private int checkDataInterval = GlobalSetting.CheckDataInterval;// 检查服务器数据到达时间间隔，默认为50ms
		private bool isHeartBeating = false;// 在2*HeartBeatInterval时间内收到HeartBeat?
		private int heartbeatNo = 0;// 心跳号码
		ConnectedEventArgs e = new ConnectedEventArgs();

		#endregion

		#region public property

		/// <summary>
		/// 客户端是否连接到服务器
		/// </summary>
		public bool IsConnected
		{
			get { return isConnected; }
		}

		/// <summary>
		/// 服务器地址
		/// </summary>
		public string Address
		{
			get { return serverAddress; }
			set { serverAddress = value; }
		}

		/// <summary>
		/// 服务器端口
		/// </summary>
		public int Port
		{
			get { return serverPort; }
			set { serverPort = value; }
		}

		/// <summary>
		/// 服务端的已经连接的Socket
		/// </summary>
		public Socket ServerSocket
		{
			get { return serverSocket; }
			set { serverSocket = value; }
		}

		/// <summary>
		/// 检查服务器数据的时间间隔，默认为50ms
		/// </summary>
		public int CheckDataInterval
		{
			get { return checkDataInterval; }
			set { checkDataInterval = value; }
		}

		#endregion

		#region Events
		/// <summary>
		/// 收到服务端的数据时会产生该事件
		/// </summary>
		public event DataReceivedHandler DataReceived;

		/// <summary>
		/// 已经连接至服务端时会产生该事件
		/// </summary>
		public event ConnectedHandler Connected;

		/// <summary>
		/// 连接已经断开事件
		/// </summary>
		public event DisconnectedHandler Disconnected;

		#endregion

		#region public method

		/// <summary>
		/// the default constructor,IsConnected=false; Connect() manually must be needed.
		/// </summary>
		public Client()
		{ }

		/// <summary>
		/// give address and port,IsConnected=false;  Connect() manually must be needed.
		/// </summary>
		/// <param name="address">Server's IP address</param>
		/// <param name="port">server's port</param>
		public Client(string address, int port)
		{
			serverAddress = address;
			serverPort = port;
		}

		/// <summary>
		/// 用已经连接的socket建立Client,IsConnected=true;  Connect() manually not be needed.
		/// </summary>
		/// <param name="connectedSocket">已经连接的socket</param>
		public Client(Socket connectedSocket)
		{
			ServerSocket = connectedSocket;
			serverAddress = ((IPEndPoint)(connectedSocket.RemoteEndPoint)).Address.ToString();
			serverPort = ((IPEndPoint)(connectedSocket.RemoteEndPoint)).Port;
			Connect();
		}

		/// <summary>
		/// 连接至服务端
		/// </summary>
		/// <returns>是否成功</returns>
		public bool Connect()
		{
			if (isConnecting) return false;
			if (isConnected) return true;
			try
			{
				isConnecting = true;

				if (serverSocket == null)
				{
					try
					{
						serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
						serverSocket.Connect(serverAddress, serverPort);
					}
					catch
					{
						serverSocket = null;
						return false;
					}
				}
				serverSocket.ReceiveTimeout = 10000;
				serverSocket.SendTimeout = 10000;
				e.RemoteEndPoint = (IPEndPoint)(serverSocket.RemoteEndPoint);

				isConnected = true;
				if (Connected != null)
				{
					try
					{
						ThreadPool.QueueUserWorkItem(new WaitCallback(OnConnected));
					}
					catch
					{
					}
				}

				threadCheckRecieved = new Thread(new ThreadStart(checkData));
				threadCheckRecieved.IsBackground = true;
				threadCheckRecieved.Name = "CheckDataReceived";
				threadCheckRecieved.Start();

				threadCheckActive = new Thread(new ThreadStart(testActive));
				threadCheckActive.IsBackground = true;
				threadCheckActive.Name = "CheckALive";
				threadCheckActive.Start();

				return true;
			}
			catch (Exception e)
			{
				System.Diagnostics.Debug.WriteLine(e.ToString());
				return false;
			}
			finally
			{
				isConnecting = false;
			}
		}

		/// <summary>
		/// 关闭客户端与服务端的连接,并触发Disconnected事件
		/// </summary>
		public void Disconnect()
		{
			disconnectWithEventFired ();
		}
		
		/// <summary>
		/// 关闭客户端，但不触发Disconnected事件
		/// </summary>
		public void DisconnectNoEventFired()
		{
			if (!isConnected) return;
			isConnected = false;

			//System.Threading.Thread.Sleep(30000);
			
			try
			{
				if (threadCheckRecieved != null) threadCheckRecieved.Abort();
				if (threadCheckActive != null) threadCheckActive.Abort();
			}
			catch (Exception err)
			{
				System.Diagnostics.Debug.WriteLine("error in client.Stop().\n" + err.ToString());
			}
			try
			{
				if (serverSocket != null)
				{
					lock (serverSocket)
					{
						serverSocket.Shutdown(SocketShutdown.Both);
						serverSocket.Close();
					}

				}
			}
			catch (Exception err)
			{
				System.Diagnostics.Debug.WriteLine(err.ToString());
			}
			finally
			{
				serverSocket = null;
			}
		}

		/// <summary>
		/// 关闭客户端
		/// </summary>
		private void disconnectWithEventFired()
		{
			if (!isConnected) return;
			isConnected = false;
			
			try
			{
				if (threadCheckRecieved != null) threadCheckRecieved.Abort();
				if (threadCheckActive != null) threadCheckActive.Abort();
			}
			catch (Exception err)
			{
				System.Diagnostics.Debug.WriteLine("error in client.Stop().\n" + err.ToString());
			}
			try
			{
				if (serverSocket != null)
				{
					lock (serverSocket)
					{
						serverSocket.Shutdown(SocketShutdown.Both);
						serverSocket.Close();
					}

				}
			}
			catch (Exception err)
			{
				System.Diagnostics.Debug.WriteLine(err.ToString());
			}
			finally
			{
				serverSocket = null;
			}
			
			try
			{
				if (this.Disconnected != null)
				{
					ThreadPool.QueueUserWorkItem(new WaitCallback(OnDisconnected));
				}
			}
			catch (Exception err)
			{
				System.Diagnostics.Debug.WriteLine(err.ToString());
			}
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
			return SendData(data);
		}

		/// <summary>
		/// 将数据发送到所有与之相连的客户
		/// </summary>
		/// <param name="data">发送的数据,必须实现IData接口,并且标记为[Serializable]</param>
		/// <returns>是否成功</returns>
		public bool BroadcastData(IData data)
		{
			return SendData(data);
		}

		/// <summary>
		/// 向服务器发送数据
		/// </summary>
		/// <param name="data">发送的数据,必须实现IData接口,并且标记为[Serializable]</param>
		/// <returns>是否成功</returns>
		public bool SendData(IData data)
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
			try
			{
				Monitor.Enter(serverSocket);
				serverSocket.Send(alldata);

				return true;
			}
			catch (Exception err)
			{
				System.Diagnostics.Debug.WriteLine("Error in Client.SendData(), Data Type:" + data.Type.ToString());
				System.Diagnostics.Debug.WriteLine (err.ToString ());
				return false;
			}
			finally
			{
				Monitor.Exit(serverSocket);
			}
		}

		#endregion

		#region private method

		private void OnConnected(object o)
		{
			if(Connected !=null)
			{
				try
				{
					Connected(this, e);
				}catch
				{
					throw;
				}
			}
		}

		private void OnDisconnected(object o)
		{
			if(Disconnected !=null)
			{
				try
				{
					Disconnected(this, e);
				}catch
				{
					throw;
				}
			}
			
		}

		private void OnDataReceived(object e)
		{
			if(DataReceived !=null)
			{
				try
				{
					DataReceived(this, (ReceivedEventArgs)e);
				}
				catch
				{
					throw;
				}
			}
			
		}

		/// <summary>
		/// 从缓存区中接受指定长度的数据
		/// </summary>
		/// <param name="data">接受的数据</param>
		/// <param name="size">要接受的数据长度</param>
		/// <returns>实际接收的数据长度</returns>
		private int receiveData(ref byte[] data, int size)
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
						Monitor.Enter(serverSocket);
						if (serverSocket.Available > 0)
							recv = serverSocket.Receive(data, total, dataleft, SocketFlags.None);
					}
					catch (Exception err)
					{
						System.Diagnostics.Debug.WriteLine(err.ToString());
					}
					finally
					{
						Monitor.Exit(serverSocket);
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
		/// 从服务器接收所有的数据，数据最大长度不超过data的长度。
		/// </summary>
		/// <param name="data">数据缓存</param>
		/// <returns>读出的数据长度</returns>
		private int GetData(ref byte[] data)
		{
			return GetData(ref data, -1);
		}

		/// <summary>
		/// 从服务器接收指定长度的数据
		/// </summary>
		/// <param name="data">数据缓存</param>
		/// <param name="length" >设置要读取的数据长度，当为-1时则读取缓存区的所有数据</param>
		/// <returns>实际读出的数据长度</returns>
		private int GetData(ref byte[] data, int length)
		{
			if (!isConnected) return 0;
			if (isDisposed) return 0;
			if (length == -1)
			{
				try
				{
					Monitor.Enter(serverSocket);

					if (serverSocket.Available > 0)
						return serverSocket.Receive(data, 0, data.Length, SocketFlags.None);
					else
						return 0;
				}
				catch (Exception err)
				{
					System.Diagnostics.Debug.WriteLine(err.ToString());
					return 0;
				}
				finally
				{
					Monitor.Exit(serverSocket);
				}
			}
			else
			{
				return this.receiveData(ref data, length);
			}
		}

		/// <summary>
		/// 监测连接是否有效:定期发送心跳
		/// </summary>
		private void testActive()
		{
			int n = 0;
			System.Threading.Thread.Sleep(10000);
			while (isConnected)
			{
				n = (n == 0) ? 1 : 0;
				try
				{
					//send heartbeat data.
					heartbeatNo = (heartbeatNo + 1) % int.MaxValue;
					Heartbeat heartbeat = new Heartbeat();
					heartbeat.No = heartbeatNo;
					isHeartBeating = false;
					try
					{
						this.SendData(heartbeat);
						serverSocket.NoDelay = true;
						this.SendData(heartbeat);
						System.Diagnostics.Debug.WriteLine(serverSocket.LocalEndPoint.ToString() + ": " + DateTime.Now.ToString() + " Send heatbeat. No=" + heartbeat.No.ToString());
						//GC.Collect();
					}
					catch (Exception err)
					{
						System.Diagnostics.Debug.WriteLine(err.ToString());
					}
					finally
					{
						serverSocket.NoDelay = false;
					}

				}
				catch (Exception err)
				{
					System.Diagnostics.Debug.WriteLine(err.ToString());
				}
				System.Threading.Thread.Sleep(GlobalSetting.HeartBeatInterval);
				if (n == 0) //是检验是否受到Heartbeat的时候了
				{
					if (isHeartBeating)
						continue;
					else //没有heartbeat
						break;
				}
			}
			new Thread(new ThreadStart(disconnectWithEventFired)).Start();
		}

		/// <summary>
		/// 检查是否有服务器数据到达,处理受到的心跳
		/// </summary>
		private void checkData()
		{
			byte[] dataL = new byte[sizeof(int)];
			int length = 0;
			bool err = false;
			while (isConnected)
			{
				try
				{
					Thread.Sleep(checkDataInterval);
					if (err)//继续等
					{
						System.Threading.Thread.Sleep(10 * checkDataInterval);
						err = false;
					}

					//从服务器有数据可读?
					if (!serverSocket.Poll(1, SelectMode.SelectRead)) continue;

					//首先获得数据的长度
					if (GetData(ref dataL, sizeof(int)) != sizeof(int))//数据 不OK
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
					if (GetData(ref data, length) == length)
					{
						try
						{
							ReceivedEventArgs eArgs = new ReceivedEventArgs();
							eArgs.RemoteEndPoint = (IPEndPoint)this.ServerSocket.RemoteEndPoint;
							MemoryStream ms = new MemoryStream(MiniLZO.Decompress(data));
							BinaryFormatter bfm = new BinaryFormatter();
							eArgs.Data = (IData)bfm.Deserialize(ms);
							ms.Close();
							Heartbeat test = new Heartbeat();
							if (eArgs.Data.Type == test.Type)//is heartbeat?
							{
								//return heartbeat data.
								isHeartBeating = true;
								//this.SendData(eArgs.Data);
								System.Diagnostics.Debug.WriteLine(serverSocket.LocalEndPoint.ToString() + ": " + DateTime.Now.ToString() + "Receive heatbeat." + ((Heartbeat)(eArgs.Data)).No.ToString());
							}
							else//no, it is a user's data.
							{
								//引发事件
								if (DataReceived != null)
									//new Thread(new ParameterizedThreadStart(OnDataReceived)).Start(eArgs);
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
				catch (Exception error)
				{
					System.Diagnostics.Debug.WriteLine("Error in Client.Checkdata:\n" + error.ToString());
					err = true;
				}
			}
		}

		#endregion

		#region IDisposable 成员

		private bool isDisposed = false;
		/// <summary>
		/// 释放对象
		/// </summary>
		/// <param name="disposing"></param>
		protected virtual void Dispose(bool disposing)
		{
			if (!isDisposed)
			{
				if (disposing)
				{
				}
				disconnectWithEventFired();
			}
			isDisposed = true;
		}

		/// <summary>
		/// 析构函数
		/// </summary>
		~Client()
		{
			Dispose(false);
		}
		/// <summary>
		/// 注销Client，释放资源
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}
}
