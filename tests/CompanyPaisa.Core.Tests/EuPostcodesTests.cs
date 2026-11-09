using System.IO.Compression;
using System.Text;
using CompanyPaisa.Importer.Eu;
using CompanyPaisa.Importer.Sec;

namespace CompanyPaisa.Core.Tests;

public class EuPostcodesTests
{
    /// <summary>Serves a GeoNames-style zip with the given rows (country, code, place, latitude, longitude).</summary>
    private sealed class FakeGeoNames(string country, params (string Code, string Place, double Lat, double Lng)[] rows) : ISecClient
    {
        public int NetworkRequests => 0;
        public Task<string?> GetStringAsync(string url, CachePolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<byte[]?> GetBytesAsync(string url, CachePolicy policy, CancellationToken ct = default)
        {
            var text = string.Join('\n', rows.Select(r => FormattableString.Invariant($"{country}\t{r.Code}\t{r.Place}\t\t\t\t\t\t\t{r.Lat}\t{r.Lng}\t4")));
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry($"{country}.txt").Open(), Encoding.UTF8))
                writer.Write(text);
            return Task.FromResult<byte[]?>(buffer.ToArray());
        }
    }

    [Fact]
    public async Task A_code_missing_from_geonames_takes_the_nearest_code_of_its_postal_area()
    {
        // Lithuania's list has LT-14300 and LT-14320 but not Apranga's LT-14311, nor anything in the 99xxx area.
        var postcodes = new EuPostcodes();
        await postcodes.LoadAsync(new FakeGeoNames("LT", ("LT-14300", "Vilnius", 54.70, 25.30), ("LT-14320", "Vilnius", 54.71, 25.31),
            ("LT-44245", "Kaunas", 54.90, 23.90)), "{0}", "LT", CancellationToken.None);

        Assert.Equal("Vilnius", postcodes.Locate("LT", "LT-14311")?.Place);
        Assert.Equal(54.71, postcodes.Locate("LT", "14318")!.Value.Point.Latitude, 2);   // 14320 is nearer than 14300
        Assert.Equal("Kaunas", postcodes.Locate("LT", "44245")?.Place);                      // exact
        Assert.Null(postcodes.Locate("LT", "99254"));                                        // no code in its area
    }
}
