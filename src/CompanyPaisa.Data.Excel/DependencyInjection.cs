using CompanyPaisa.Core;
using CompanyPaisa.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyPaisa.Data.Excel;

public static class DependencyInjection
{
    /// <summary>Registers the Excel workbook as the <see cref="ICompanyRepository"/>.</summary>
    public static IServiceCollection AddExcelDataSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<ExcelDataSourceOptions>(configuration, ExcelDataSourceOptions.SectionName);
        services.AddSingleton<ICompanyRepository, ExcelCompanyRepository>();
        return services;
    }
}
