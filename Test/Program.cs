using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using GZNU.NetworkServer;
using GZNU.Interfaces;
using SimmoTech.Utils.Serialization;

namespace Test
{
	[Serializable ]
	class Hello:IData
	{
		public override string ToString()
		{
			return "Hello!";
		}
		
		public int Type {
			get {
				return 0;
			}
			set {
				;
			}
		}
	}
	
	//使用Serializable属性的默认可序列化
	[Serializable]
	class MyHome
	{
		string Name="HappyZone";
		string Address="Guiyang,Guizhou";
		
		public override string ToString()
		{
			return "Class \'MyHome\'\n\tName: "+Name+", Address: "+Address +".";
		}
	}
	
	//也可以使用SimmoTech.Utils.Serialization命名空间的自定义序列化。
	[Serializable]
	class MyDaughter:System.Runtime.Serialization.ISerializable
	{
		string Name="Mei";
		DateTime Birthday=new DateTime (2008,6,1,20,35,0);

		public MyDaughter ()
		{
		}
		
		public override string ToString()
		{
			return "Class \'MyDaughter\'\n\tName: "+Name+", Birthday: "+Birthday.ToString()+".";
		}
		
		public void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
		{
			SerializationWriter w=new SerializationWriter ();
			w.Write (Name );
			w.Write (Birthday );
			info.AddValue ("1",w.ToArray ());
		}
		
		//序列化构造函数，用于Deserialize，如果没有实现该构造函数，则不能解析对方发送来的该类数据。
		private MyDaughter (System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
		{
			SerializationReader r=new SerializationReader ((byte[])(info.GetValue("1", typeof(byte[]))));
			Name=r.ReadString ();
			Birthday=r.ReadDateTime ();
		}
	}
	
	[Serializable ]
	class Data:IData
	{
		//public构造函数
		public Data()
		{
			No=0;
			home=new MyHome ();
			daughter =new MyDaughter ();
			for(int i=0;i<List.Length ;i++)
				List[i]=i;
		}
		
		//携带的数据，可以是可序列化与反序列化的各种数据
		int No;
		MyHome home;//默认序列化
		MyDaughter daughter;//自定义序列化
		double[] List=new double [655350] ;
		
		public override string ToString()
		{
			return "Class \'Data\'\n\tNo:"+ No.ToString ()+"\n\thome:"+home.ToString ()+"\n\tdaughter:"+daughter .ToString ()+"\n\tList length: "+List.Length.ToString ()+", the last element is "+List[List.Length-1].ToString ();
		}
		
		//实现IData接口
		#region IData
		private int type=1;
		public int Type {
			get {
				return type ;
			}
			set {
				;
			}
		}
		#endregion
	}
	
	[Serializable ]
	class Data1:IData,System.Runtime.Serialization.ISerializable
	{
		//public构造函数，由于有了private构造函数，故必须声明public的构造函数
		public Data1()
		{
			No=0;
			home=new MyHome ();
			daughter =new MyDaughter ();
			for(int i=0;i<List.Length ;i++)
				List[i]=i;
		}
		
		//携带的数据，可以是可序列化与反序列化的各种数据
		int No;
		MyHome home;//默认序列化
		MyDaughter daughter;//自定义序列化
		double[] List=new double [655350] ;

		public override string ToString()
		{
			return "Class \'Data\'\n\tNo:"+ No.ToString ()+"\n\thome:"+home.ToString ()+"\n\tdaughter:"+daughter .ToString ()+"\n\tList length: "+List.Length.ToString ()+", the last element is "+List[List.Length-1].ToString ();
		}
		
		//实现IData接口
		#region IData
		private int type=1;
		public int Type {
			get {
				return type ;
			}
			set {
				;
			}
		}
		#endregion
		
		#region ISerializable
		public void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
		{
			System.Runtime.Serialization.Formatters.Binary.BinaryFormatter f=new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter ();
			System.IO.MemoryStream ms=new System.IO.MemoryStream ();
			SerializationWriter w = new SerializationWriter();
			
			w.Write(No);
			
			f.Serialize (ms,home );
			w.Write(ms.ToArray ());
			
			ms.Close();
			ms=new System.IO.MemoryStream ();
			f.Serialize (ms,daughter );
			w.Write(ms.ToArray());
			
			w.Write (List);
			
			info.AddValue("1", w.ToArray());
		}
		#endregion
		
		//序列化构造函数,如果没有实现该构造函数，则不能解析对方发送来的该类数据。
		private Data1(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
		{
			type = 1 ;
			SerializationReader r = new SerializationReader((byte[])(info.GetValue("1", typeof(byte[]))));
			No = r.ReadInt32();
			
			System.Runtime.Serialization.Formatters.Binary.BinaryFormatter f=new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter ();
			System.IO.MemoryStream ms=new System.IO.MemoryStream (r.ReadByteArray ());
			home=(MyHome)f.Deserialize (ms);
			
			ms.Close ();
			ms=new System.IO.MemoryStream (r.ReadByteArray ());
			daughter =(MyDaughter )f.Deserialize (ms);
			
			List =r.ReadDoubleArray ();
		}
	}
	
	class Program
	{
		static Server s;
		static Client c1,c2;
		
		static void Main(string[] args)
		{
			//要传送的数据。该数据必须支持序列化与反序列化。
			Data d=new Data ();
			Data1 d1=new Data1();
			s = new Server();
			s.Port = 9555;
			
			//当服务端收到数据时
			s.DataReceived +=new DataReceivedHandler(s_DataReceived);
			//当客户端连接至服务器时
			s.Connected +=new ConnectedHandler(s_Connected);
			//当有客户端断开时
			s.Disconnected +=new DisconnectedHandler(s_Disconnected);
			
			if(s.Connect())
				Console.WriteLine ("The server is ok.");
			else
			{
				Console.WriteLine ("Server fail.");
				return;
			}
			//至此，服务端已经开始了。

			c1=new Client();
			c1.Address ="localhost";
			c1.Port =9555;
			
			c2=new Client ();
			c2.Address ="localhost";
			c2.Port =9555;
			
			//当与服务端建立连接时
			c1.Connected+=new ConnectedHandler(c1_Connected);
			//当收到服务端的数据时
			c1.DataReceived +=new DataReceivedHandler(c1_DataReceived);
			//当与服务端断开时
			c1.Disconnected +=new DisconnectedHandler(c1_Disconnected);
			
			c2.Connected+=new ConnectedHandler(c1_Connected);
			c2.DataReceived +=new DataReceivedHandler(c1_DataReceived);
			c2.Disconnected +=new DisconnectedHandler(c1_Disconnected);
			
			//连至服务端
			if(c1.Connect())
			{
				c1.SendData (d);
			}
			else
				Console.WriteLine ("c1 connect to server fail.");
			
			if(c2.Connect())
			{
				c1.SendData (d1);
			}
			else
				Console.WriteLine ("c2 connect to server fail.");
			
			Console.WriteLine ("Wait a minute, you will see the data...");
			System.Threading.Thread.Sleep (5000);
			
			Console.WriteLine ("Press any key, the client will disconnect to the server.");
			Console.ReadKey ();
			c1.Disconnect();
			System.Threading.Thread.Sleep (10000);
			
			
			Console.WriteLine ("Press any key to reconnect to server and send data.");
			Console.ReadKey ();
			if(c1.Connect())
				c1.SendData (d);
			else
				Console.WriteLine ("connect to server fail.");
			System.Threading.Thread.Sleep (5000);
			Console.WriteLine ("Press any key to shut down the server.");
			Console.ReadKey();
			s.Disconnect();
			
			Console.WriteLine ("Wait the client to disconnect...");
			System.Threading.Thread.Sleep (10000);
			Console.WriteLine ("Press any key to exit.");
			Console.ReadKey();

		}

		static void s_Disconnected(object sender, ConnectedEventArgs e)
		{
			Console.WriteLine (DateTime.Now.ToString()+" Server: "+e.RemoteEndPoint.ToString ()+" disconnect.");
		}

		static void s_Connected(object sender, ConnectedEventArgs e)
		{
			Console.WriteLine (DateTime.Now.ToString()+" Server: "+e.RemoteEndPoint .ToString ()+" connected.");
			s.SendData (new Hello(),e.RemoteEndPoint.Address.ToString(),e.RemoteEndPoint.Port);
		}

		static void s_DataReceived(object sender, ReceivedEventArgs e)
		{
			Console.WriteLine (DateTime.Now.ToString()+" Server: receive data from "+e.RemoteEndPoint.ToString ());
			Console.WriteLine (e.Data.ToString ());
		}

		static void c1_Disconnected(object sender, ConnectedEventArgs e)
		{
			Console.WriteLine (DateTime.Now.ToString()+" Client: disconnect to server "+e.RemoteEndPoint.ToString ()+".");
		}

		static void c1_Connected(object sender, ConnectedEventArgs e)
		{
			Console.WriteLine (DateTime.Now.ToString()+" Client: connect to server "+e.RemoteEndPoint.ToString ()+".");
		}

		static void c1_DataReceived(object sender, ReceivedEventArgs e)
		{
			Console.WriteLine (DateTime.Now.ToString()+" Client: receive data from "+e.RemoteEndPoint.ToString ());
			Console.WriteLine (e.Data.ToString ());
		}


	}
}
