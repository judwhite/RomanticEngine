namespace RomanticEngine.Core;

public interface ISystemInfo
{
    int MaxThreads { get; }
    int MaxHashMb { get; }
}

public class ProductionSystemInfo : ISystemInfo
{
    public int MaxThreads => Environment.ProcessorCount;
    public int MaxHashMb => 1024 * 128; // 128 GB default max, or compute based on RAM
}
