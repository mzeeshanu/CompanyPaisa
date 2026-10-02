using CompanyPaisa.Core.Abstractions;

namespace CompanyPaisa.Infrastructure.Services;

public sealed class DataChangeSignal : IDataChangeSignal
{
    private long _generation;
    public long Generation => Interlocked.Read(ref _generation);
    public void Signal() => Interlocked.Increment(ref _generation);
}
