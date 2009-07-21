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
  �ṩ��һϵ��C/S�ܹ��Ļ�����
  �ڸ������ṩ�˻����¼��������շ�����
 */
/*
 *This project used ManagedLZO.MiniLZO to compress and decompress.
 *Thanks for Markus Franz Xaver Johannes Oberhumer and Shane Eric Bryldt.
 *You can get it on http://www.codeproject.com/KB/recipes/managedlzo.aspx.
 */

namespace GZNU.NetworkServer
{
	/// <summary>
	/// �����
	/// </summary>
	public class Server : IDisposable, INode
	{
		/// <summary>
		/// ����������˵�ÿ���ͻ��ˡ�
		/// </summary>
		internal class Client
		{
			public Socket clientSocket;
			//////��������ʱ������Ӧ������2����keepAliveInterval
			public DateTime clientHearbeatTime;//�ͻ������µ�����ʱ�䣭���յ�ʱ��
			public DateTime serverHearbeatTime;//��������µ�����ʱ��--����ʱ��

			public Client(Socket clientSocket, DateTime clientHeartbeatTime, DateTime serverHeatbeatTime)
			{
				this.clientSocket = clientSocket;
				this.clientHearbeatTime = clientHeartbeatTime;
				this.serverHearbeatTime = serverHeatbeatTime;
			}
		}

		#region private member
		private object lockForClients = new object();//����clients��ͬ������

		private int listenInterval = GlobalSetting.ListenInterval;  // �����ͻ��˵�ʱ������Ĭ��Ϊ100ms
		private int maxConnections = GlobalSetting.MaxPendingConnections;//�ܽ��ܵ�������������
		private int keepAliveInterval = GlobalSetting.HeartBeatInterval;//��������ʱ������Ӧ��ȫ������

		private Hashtable IP2Index = new Hashtable();// �ͻ���--index�б�
		private ArrayList clients = new ArrayList();
		private int port = 9501;// Ĭ�϶˿ں�
		private bool isConnected = false;//������Ƿ�ʼ����

		private Thread threadListen;//���������߳�
		private Thread threadHeartbeat;//�����߳�
		private Thread threadDataReceive;//���ݽ���

		private Heartbeat heartbeat = new Heartbeat();
		#endregion

		#region public property
		/// <summary>
		/// �����ͻ��˵�ʱ������Ĭ��Ϊ500ms
		/// </summary>
		public int ListenInterval
		{
			get { return listenInterval; }
			set { listenInterval = value; }
		}

		/// <summary>
		/// ��ѯ�����Ƿ�ʼ
		/// </summary>
		public bool IsConnected
		{
			get { return isConnected; }
		}

		#endregion

		#region private method

		/// <summary>
		/// ����Connected�¼�
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
		/// ����Disconnected�¼�
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
		/// ����DataReceived�¼�
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
		/// ���������ÿ������
		/// </summary>
		private void listenClient()
		{
			while (isConnected)
			{
				try
				{
					while (isConnected && !Serversocket.Pending())
						System.Threading.Thread.Sleep(this.listenInterval);
					if (!isConnected) break;//������ͣ��

					//������ʱ
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
		/// ������ӵĿͻ����Ƿ����,����ͻ����б�
		/// </summary>
		private void keepAlive()
		{
			int n = 0;
			TimeSpan interval;
			//System.Threading.Thread.Sleep(10000);//ʹ�ͻ������㹻��ʱ�佫״̬ת����isConnected=true.
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

				if (n == 0) //�Ǽ����Ƿ��ܵ�Heartbeat��ʱ����
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
							//�ӷ����������ݿɶ�?
							if (((Client)clients[i]).clientSocket.Available == 0) continue;

							//���Ȼ�����ݵĳ���
							if (GetData(((Client)clients[i]).clientSocket, ref dataL, sizeof(int)) != sizeof(int))//���� ��OK
							{
								err = true;
								continue;
							}
							//����OK
							length = BitConverter.ToInt32(dataL, 0);
							if (length <= 0)//���ݳ�����<= 0
							{
								err = true;
								continue;
							}

							byte[] data = new byte[length];
							//��ȡ��������
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
										//���¿ͻ��˵�����������ʱ��
										((Client)clients[i]).clientHearbeatTime = DateTime.Now;
									}
									else//no, it is user's data.
									{
										//�����¼�
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
		/// �����еĿͻ��˷���������
		/// </summary>
		/// <param name="heartbeat"></param>
		/// <returns>�Ƿ�ɹ���</returns>
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
		/// �������˿�
		/// </summary>
		public int Port
		{
			set { port = value; }
			get { return port; }
		}

		/// <summary>
		/// �����յĹ�����������Ĭ��Ϊ50��
		/// </summary>
		public int MaxConnections
		{
			get { return maxConnections; }
			set { maxConnections = value; }
		}

		#endregion

		#region public member

		/// <summary>
		/// ����������socket
		/// </summary>
		public TcpListener Serversocket;

		#endregion

		#region public Events

		/// <summary>
		/// �����յ��¼�,�յ��ͻ�������ʱ��������¼�
		/// </summary>
		public event DataReceivedHandler DataReceived;

		/// <summary>
		/// �ͻ��������Ѿ������¼�
		/// </summary>
		public event ConnectedHandler Connected;

		/// <summary>
		/// �ͻ��������Ѿ��Ͽ��¼�
		/// </summary>
		public event DisconnectedHandler Disconnected;

		#endregion

		#region public method

		/// <summary>
		/// ���캯��
		/// </summary>
		public Server()
		{
			clients = ArrayList.Synchronized(clients);
		}

		/// <summary>
		/// ��������
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

				//Ӧ����һ���߳���������ӵ�������
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
		/// ֹͣ����
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
		/// ��ÿ���ͻ��˷�������
		/// </summary>
		/// <param name="data">���͵�����,����ʵ��IData�ӿ�,���ұ��Ϊ[Serializable]</param>
		/// <returns>�Ƿ�ɹ�</returns>
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
		/// ��ÿ���ͻ��˷�������
		/// </summary>
		/// <param name="data">���͵�����,����ʵ��IData�ӿ�,���ұ��Ϊ[Serializable]</param>
		/// <returns>�Ƿ�ɹ�</returns>
		public bool SendData(IData data)
		{
			return BroadcastData(data);
		}

		/// <summary>
		/// ��ָ���Ľڵ㷢������
		/// </summary>
		/// <param name="data">���͵�����,����ʵ��IData�ӿ�,���ұ��Ϊ[Serializable]</param>
		/// <param name="address">IP��ַ</param>
		/// <param name="port">�˿�</param>
		/// <returns>�Ƿ�ɹ�</returns>
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

		#region IDisposable ��Ա
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
		/// ��������
		/// </summary>
		~Server()
		{
			Disconnect();
		}
		/// <summary>
		/// ע��Server���ͷ���Դ
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		#endregion
	}
}
