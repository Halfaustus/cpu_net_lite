using cpu_net.Model;
using System.Collections.Generic;

namespace cpu_net.ViewModel
{
    public class CarrierViewModel
    {
        public IReadOnlyList<CarriersModel> ComboxList { get; } = new[]
        {
            new CarriersModel { Key = 0, Text = "请选择运营商" },
            new CarriersModel { Key = 1, Text = "移动" },
            new CarriersModel { Key = 2, Text = "联通" },
            new CarriersModel { Key = 3, Text = "电信" }
        };
    }
}
