// Generates data/sample/companypaisa.sample.xlsx — the development seed workbook.
// Company names and approximate locations are real; ALL FINANCIAL FIGURES AND EXECUTIVE NAMES ARE SYNTHETIC.
// Usage (from the repo root):  dotnet run --project tools/CompanyPaisa.SampleData [outputPath]

using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Data.Excel;

var output = args.Length > 0 ? args[0] : Path.Combine("data", "sample", "companypaisa.sample.xlsx");

// ticker, name, exchange, sector, site label, location type, city, lat, lng, revenue ($B), growth/yr, net margin
var seed = new (string T, string N, string Ex, string Sec, string Site, LocationType Type, string City, double Lat, double Lng, double Rev, double G, double M)[]
{
    ("ADBE","Adobe","NASDAQ","Software","Lehi campus",LocationType.Campus,"Lehi",40.4295,-111.8935,21.5,.10,.26),
    ("TXN","Texas Instruments","NASDAQ","Semiconductors","LFAB wafer fab",LocationType.Plant,"Lehi",40.4165,-111.8720,15.6,-.06,.30),
    ("WEAV","Weave Communications","NYSE","Software","Headquarters",LocationType.Headquarters,"Lehi",40.4330,-111.9005,.21,.19,-.13),
    ("PRPL","Purple Innovation","NASDAQ","Consumer products","Headquarters",LocationType.Headquarters,"Lehi",40.4355,-111.8660,.49,-.04,-.08),
    ("NATR","Nature's Sunshine","NASDAQ","Health & wellness","Headquarters",LocationType.Headquarters,"Lehi",40.4245,-111.8820,.45,.03,.04),
    ("OWLT","Owlet","NYSE","Health & wellness","Headquarters",LocationType.Headquarters,"Lehi",40.4275,-111.8585,.08,.15,-.25),
    ("LFVN","LifeVantage","NASDAQ","Health & wellness","Headquarters",LocationType.Headquarters,"Lehi",40.4338,-111.8880,.23,.15,.05),
    ("RUN","Sunrun","NASDAQ","Energy","Lehi office",LocationType.Office,"Lehi",40.4262,-111.8905,2.1,.05,-.2),
    ("VRSK","Verisk (Xactware)","NASDAQ","Software","Xactware campus",LocationType.Campus,"Lehi",40.4385,-111.8775,2.9,.08,.30),
    ("DOMO","Domo","NASDAQ","Software","Headquarters",LocationType.Headquarters,"American Fork",40.3935,-111.7905,.32,-.01,-.25),
    ("HSIC","Henry Schein One","NASDAQ","Healthcare","Henry Schein One office",LocationType.Office,"American Fork",40.3842,-111.8190,12.7,.02,.03),
    ("NUS","Nu Skin","NYSE","Health & wellness","Headquarters",LocationType.Headquarters,"Provo",40.2345,-111.6585,1.7,-.12,.03),
    ("NRG","NRG Energy (Vivint)","NYSE","Energy","Vivint campus",LocationType.Campus,"Provo",40.3010,-111.6660,28.1,.02,.04),
    ("HQY","HealthEquity","NASDAQ","Finance","Headquarters",LocationType.Headquarters,"Draper",40.5205,-111.8710,1.2,.2,.09),
    ("EBAY","eBay","NASDAQ","Consumer products","Draper office",LocationType.Office,"Draper",40.5235,-111.8860,10.3,.02,.19),
    ("PRG","PROG Holdings","NYSE","Finance","Headquarters",LocationType.Headquarters,"Draper",40.5140,-111.8990,2.4,0,.07),
    ("NICE","NICE (CXone)","NASDAQ","Software","Sandy office",LocationType.Office,"Sandy",40.5612,-111.8930,2.8,.12,.17),
    ("MMSI","Merit Medical","NASDAQ","Healthcare","Headquarters",LocationType.Headquarters,"South Jordan",40.5610,-111.9710,1.36,.08,.09),
    ("CRCT","Cricut","NASDAQ","Consumer products","Headquarters",LocationType.Headquarters,"South Jordan",40.5540,-111.9020,.71,-.05,.08),
    ("SPWH","Sportsman's Warehouse","NASDAQ","Consumer products","Headquarters",LocationType.Headquarters,"West Jordan",40.6030,-111.9620,1.2,-.06,-.03),
    ("BYON","Beyond","NYSE","Consumer products","Headquarters",LocationType.Headquarters,"Midvale",40.6195,-111.8900,1.4,-.2,-.15),
    ("UTMD","Utah Medical Products","NASDAQ","Healthcare","Headquarters",LocationType.Headquarters,"Midvale",40.6110,-111.8960,.04,-.04,.35),
    ("EXR","Extra Space Storage","NYSE","Real estate","Headquarters",LocationType.Headquarters,"Cottonwood Heights",40.6205,-111.8105,3.3,.1,.26),
    ("SNFCA","Security National Financial","NASDAQ","Finance","Headquarters",LocationType.Headquarters,"Murray",40.6590,-111.8990,.33,.05,.07),
    ("USNA","USANA Health Sciences","NYSE","Health & wellness","Headquarters",LocationType.Headquarters,"Salt Lake City",40.6905,-111.9405,.85,-.03,.07),
    ("FC","Franklin Covey","NYSE","Education","Headquarters",LocationType.Headquarters,"Salt Lake City",40.7005,-111.9305,.28,.02,.07),
    ("COOK","Traeger","NYSE","Consumer products","Headquarters",LocationType.Headquarters,"Salt Lake City",40.7110,-111.8560,.6,-.02,-.05),
    ("VREX","Varex Imaging","NASDAQ","Healthcare","Headquarters",LocationType.Headquarters,"Salt Lake City",40.7505,-111.9510,.81,-.05,.02),
    ("CLAR","Clarus","NASDAQ","Consumer products","Headquarters",LocationType.Headquarters,"Salt Lake City",40.7300,-111.9000,.26,-.1,-.05),
    ("MYGN","Myriad Genetics","NASDAQ","Healthcare","Headquarters",LocationType.Headquarters,"Salt Lake City",40.7610,-111.8310,.84,.1,-.12),
    ("RXRX","Recursion","NASDAQ","Healthcare","Headquarters",LocationType.Headquarters,"Salt Lake City",40.7655,-111.8960,.06,.3,-5),
    ("PDYN","Palladyne AI","NASDAQ","Software","Headquarters",LocationType.Headquarters,"Salt Lake City",40.7560,-111.9070,.01,.5,-3),
    ("ZION","Zions Bancorporation","NASDAQ","Finance","Headquarters",LocationType.Headquarters,"Salt Lake City",40.7685,-111.8905,3.1,.03,.22),
    ("GS","Goldman Sachs","NYSE","Finance","Salt Lake City office",LocationType.Office,"Salt Lake City",40.7660,-111.8915,53.5,.08,.27),
};

var companies = new List<Company>();
var locations = new List<CompanyLocation>();
var financials = new List<FinancialPeriod>();
var executives = new List<ExecutiveCompensation>();
var people = new List<Person>();
(string Title, string Role, double Factor)[] Roles =
[
    ("Chief Executive Officer", "CEO", 1.0),
    ("Chief Financial Officer", "CFO", .34),
    ("Chief Operating Officer", "COO", .28),
];
var seats = new Dictionary<(string Company, string Role), string>();   // original holder of each seat
var asOf = new DateOnly(2026, 6, 30);

foreach (var s in seed)
{
    var rnd = new Random(StableHash(s.T));
    companies.Add(new Company
    {
        CompanyId = s.T, Name = s.N, Ticker = s.T, Exchange = s.Ex, Sector = s.Sec,
        Description = "Sample record for development. Financial figures are synthetic.",
        AsOfDate = asOf
    });
    locations.Add(new CompanyLocation
    {
        LocationId = $"{s.T}-001", CompanyId = s.T, Type = s.Type, Label = s.Site,
        City = s.City, State = "UT", Point = new GeoPoint(s.Lat, s.Lng)
    });

    // 40 quarters: Q3 2016 → Q2 2026, growing at s.G per year into s.Rev (annual, $B).
    var quarters = new List<FinancialPeriod>();
    for (var i = 0; i < 40; i++)
    {
        var yearsBack = (39 - i) / 4.0;
        var baseRev = s.Rev * 1e9 / 4 / Math.Pow(1 + s.G, yearsBack);
        var revenue = baseRev * (1 + .035 * Math.Sin(i * Math.PI / 2)) * (1 + (rnd.NextDouble() - .5) * .05);
        var net = revenue * (s.M + (rnd.NextDouble() - .5) * .06);
        quarters.Add(new FinancialPeriod
        {
            CompanyId = s.T, PeriodType = PeriodType.Quarterly,
            FiscalYear = 2016 + (2 + i) / 4, FiscalQuarter = (2 + i) % 4 + 1,
            Revenue = Math.Round((decimal)revenue), NetIncome = Math.Round((decimal)net)
        });
    }
    financials.AddRange(quarters);
    financials.AddRange(quarters.Where(q => q.FiscalYear is >= 2017 and <= 2025).GroupBy(q => q.FiscalYear).Select(g => new FinancialPeriod
    {
        CompanyId = s.T, PeriodType = PeriodType.Annual, FiscalYear = g.Key,
        Revenue = g.Sum(q => q.Revenue), NetIncome = g.Sum(q => q.NetIncome)
    }));

    // Three executive seats, 10 years (2016-2025). Pay scales with company size. Names are placeholders on purpose.
    var scale = Math.Sqrt(s.Rev / 20);
    foreach (var (title, role, factor) in Roles)
    {
        var personId = NewPerson();
        seats[(s.T, role)] = personId;
        for (var j = 0; j < 10; j++)
        {
            var total = (1.6 + 21 * scale) * factor * (.6 + .05 * j) * (.85 + rnd.NextDouble() * .3) * 1e6;
            var salary = Math.Min(total * .32, (.55 + .5 * scale) * Math.Max(factor, .6) * 1e6);
            var bonus = total * (.08 + rnd.NextDouble() * .08);
            var other = total * .03;
            executives.Add(new ExecutiveCompensation
            {
                CompanyId = s.T, PersonId = personId, ExecutiveName = NameOf(personId),
                Title = title, Year = 2016 + j,
                Salary = Math.Round((decimal)salary), Bonus = Math.Round((decimal)bonus), Other = Math.Round((decimal)other),
                StockAwards = Math.Round((decimal)(total - salary - bonus - other)), Total = Math.Round((decimal)total)
            });
        }
    }
}

// Career moves: a CFO/COO at one company becomes CEO of another from a given year.
// The mover takes over the destination seat; a new person fills the seat they left.
var moves = new (string FromCo, string FromRole, string ToCo, string ToRole, int Year)[]
{
    ("NUS", "CFO", "LFVN", "CEO", 2021), ("ADBE", "COO", "WEAV", "CEO", 2020), ("TXN", "CFO", "PRPL", "CEO", 2022),
    ("EBAY", "COO", "HQY", "CEO", 2019), ("ZION", "CFO", "PRG", "CEO", 2021), ("USNA", "COO", "NATR", "CEO", 2023),
    ("VRSK", "CFO", "DOMO", "CEO", 2022), ("GS", "COO", "EXR", "CEO", 2020),
};
foreach (var m in moves)
{
    var mover = seats[(m.FromCo, m.FromRole)];
    var successor = NewPerson();
    for (var i = 0; i < executives.Count; i++)
    {
        var e = executives[i];
        if (e.Year < m.Year) continue;
        if (e.CompanyId == m.FromCo && e.PersonId == mover)
            executives[i] = e with { PersonId = successor, ExecutiveName = NameOf(successor) };
        else if (e.CompanyId == m.ToCo && e.Title == TitleOf(m.ToRole))
            executives[i] = e with { PersonId = mover, ExecutiveName = NameOf(mover) };
    }
}

ExcelWorkbookWriter.Write(output, companies, locations, financials, executives, people, new Dictionary<string, string>
{
    ["data_version"] = "sample-2026.09",
    ["as_of_date"] = asOf.ToString("yyyy-MM-dd"),
    ["is_sample"] = "true",
    ["note"] = "Synthetic figures for development only. Company names and approximate locations are real."
});

Console.WriteLine($"Wrote {companies.Count} companies, {financials.Count} financial rows, {executives.Count} executive pay rows, {people.Count} people to {Path.GetFullPath(output)}");

string NewPerson()
{
    var id = $"P{people.Count + 1:000}";
    people.Add(new Person { PersonId = id, Name = $"Sample Executive {people.Count + 1:000}" });
    return id;
}

string NameOf(string personId) => people.First(p => p.PersonId == personId).Name;

string TitleOf(string role) => Roles.First(r => r.Role == role).Title;

static int StableHash(string s)
{
    unchecked
    {
        var h = (int)2166136261;
        foreach (var ch in s) h = (h ^ ch) * 16777619;
        return h;
    }
}
