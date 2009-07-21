
namespace GZNU.Interfaces
{
    /// <summary>
    /// 收发的数据
    /// </summary>
    public interface IData 
    {
        /// <summary>
        /// 数据的类型，用于区分不同的数据
        /// </summary>
        int Type { get; set; }
    }
}
