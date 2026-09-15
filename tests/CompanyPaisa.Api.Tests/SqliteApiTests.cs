using System.Text.Json.Nodes;

namespace CompanyPaisa.Api.Tests;

/// <summary>
/// The API must answer identically whether the data comes from the Excel sample or the same rows in SQLite: the
/// storage is swappable, the website shouldn't be able to tell.
/// </summary>
public class SqliteApiTests(SampleDataFactory excel, SqliteSampleDataFactory sqlite)
    : IClassFixture<SampleDataFactory>, IClassFixture<SqliteSampleDataFactory>
{
    [Theory]
    [InlineData("/api/v1/companies/near?near=84043&radiusMiles=25&pageSize=200")]
    [InlineData("/api/v1/companies/LFVN")]
    [InlineData("/api/v1/companies/LFVN/financials?period=Quarterly&years=3")]
    [InlineData("/api/v1/companies/LFVN/financials?period=Annual")]
    [InlineData("/api/v1/companies/LFVN/executives?years=5")]
    [InlineData("/api/v1/executives/near?near=84043&radiusMiles=25&pageSize=200")]
    [InlineData("/api/v1/sectors")]
    public async Task Sqlite_and_excel_give_the_same_answers(string path)
    {
        var fromExcel = await Json(excel, path);
        var fromSqlite = await Json(sqlite, path);
        Assert.Equal(fromExcel, fromSqlite);
    }

    [Fact]
    public async Task Health_reports_the_database_loaded()
    {
        var response = await sqlite.CreateClient().GetAsync("/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> Json(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string path)
    {
        var node = JsonNode.Parse(await factory.CreateClient().GetStringAsync(path))!;
        if (node is JsonObject o) o.Remove("dataVersion");   // says where the data came from; everything else must match
        return node.ToJsonString();
    }
}
