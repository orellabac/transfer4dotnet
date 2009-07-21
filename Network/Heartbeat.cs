using System;
using GZNU.Interfaces;
using System.Runtime.Serialization;
using SimmoTech.Utils.Serialization;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace GZNU.NetworkServer
{
	[Serializable]
	internal class Heartbeat : IData,ISerializable
	{
		/// <summary>
		/// 编号
		/// </summary>
		public int No;

		public Heartbeat ()
		{
		}

		#region IData 成员
		private int type = int.MinValue;
		public int Type
		{
			get
			{
				return type;
			}
			set
			{
				;
			}
		}
		
		public void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
		{
			SerializationWriter w = new SerializationWriter();
			w.Write(No);
			info.AddValue("1", w.ToArray());
		}
		
		private Heartbeat(SerializationInfo info, StreamingContext context)
		{
			type = int.MinValue;
			SerializationReader r = new SerializationReader((byte[])(info.GetValue("1", typeof(byte[]))));
			No = r.ReadInt32();
		}
		#endregion
	}

}
