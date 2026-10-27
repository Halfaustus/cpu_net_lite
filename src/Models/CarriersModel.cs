namespace cpu_net.Model
{
    public class CarriersModel
    {
        // 0=未选择，1=移动，2=联通，3=电信；选项创建后不修改。
        public int Key { get; init; }
        public string Text { get; init; } = string.Empty;
    }
}
