namespace CompanyPaisa.Core.Abstractions;

/// <summary>
/// The only way the application reads company data. Implementations: SQLite (the importers' database), Excel (workbooks).
/// Nothing above this interface knows where the data lives.
/// </summary>
/// <remarks>
/// This is the whole data set, composed from one interface per kind of data. Depend on the narrowest facet a class
/// actually needs (<see cref="IFinancialsReader"/>, <see cref="IJobSalaryReader"/>…) rather than on all of it.
/// Every facet is required: a data source with nothing to return says so by returning an empty list, so that adding
/// a kind of data breaks a source that has not handled it instead of silently serving nothing.
/// </remarks>
public interface ICompanyRepository :
    ICompanyReader,
    ICompanyLocationReader,
    IFinancialsReader,
    IExecutivePayReader,
    IWorkerPayReader,
    IJobSalaryReader;
