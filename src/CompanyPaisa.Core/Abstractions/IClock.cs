namespace CompanyPaisa.Core.Abstractions;

/// <summary>Current time, abstracted so date logic can be tested.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
