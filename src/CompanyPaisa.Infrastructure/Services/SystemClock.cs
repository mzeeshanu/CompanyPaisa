using CompanyPaisa.Core.Abstractions;

namespace CompanyPaisa.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
