using CompanyPaisa.Importer.Compensation;
using CompanyPaisa.Importer.Salaries;

namespace CompanyPaisa.Core.Tests;

/// <summary>The CEO pay ratio reader, and the rules that turn H-1B wage filings into salaries by job title.</summary>
public class WorkforcePayTests
{
    [Theory]
    // Prose, "N to 1"
    [InlineData("For 2025, the median of the annual total compensation of all employees was $68,254 and the annual total compensation of our CEO was $63,209,845. The ratio is 926 to 1.", 68_254, 63_209_845, 926)]
    // A table: the CEO's figure without a $ sign, "N to 1" with a decimal
    [InlineData("Median Employee 2025 annual total compensation $64,408 Mr. Cuomo (\"PEO\") 2025 annual total compensation 8,965,642 Ratio of PEO to Median Employee annual total compensation 139.2 to 1", 64_408, 8_965_642, 139.2)]
    // Reversed: "1:44.7"
    [InlineData("2021 total annual compensation for the median employee was $94,072 • 2021 total annual compensation for Mr. Rivers, the Chief Executive Officer, was $4,208,543 • The result is a median employee to Chief Executive Officer pay ratio of 1:44.7", 94_072, 4_208_543, 44.7)]
    // A bare ratio in a table row
    [InlineData("Pay ratio Annual total compensation of the median employee for 2025 $123,976 Annual total compensation of the CEO for 2025 $11,819,204 Ratio of annual total compensation of the median employee to the annual total compensation of CEO for 2025 95.3 Pay versus performance", 123_976, 11_819_204, 95.3)]
    public void Reads_the_pay_ratio_when_the_numbers_agree(string text, int median, int ceo, double ratio)
    {
        var found = PayRatioReader.Read($"<p>{text}</p>");

        Assert.NotNull(found);
        Assert.Equal(median, found.MedianEmployeePay);
        Assert.Equal(ceo, found.CeoPay);
        Assert.Equal((decimal)ratio, found.Ratio);
    }

    [Fact]
    public void Leaves_out_a_ratio_the_amounts_dont_support()
    {
        // 5,000,000 / 50,000 = 100, not 250: a misread or a supplemental figure, so nothing is kept.
        Assert.Null(PayRatioReader.Read("<p>CEO pay ratio: the median employee earned $50,000 and the CEO $5,000,000, a ratio of 250 to 1.</p>"));
        // Thor 2025: a head count after the heading isn't a ratio, however well two amounts happen to divide.
        Assert.Null(PayRatioReader.Read("<p>CEO PAY RATIO As of July 31, 2025 we had approximately 13,200 U.S. employees and 7,700 non-U.S. employees. " +
                                        "A benefit of $1,138 was paid. Our CEO's total compensation was $14,952,360.</p>"));
        // A CEO figure far from every total in the proxy's own pay table is the wrong amount; the table's figure is used.
        var amazon = PayRatioReader.Read("<p>Pay ratio: the median employee earned $47,990 and $53,211 was matched 1 to 1; the ratio was 34 to 1.</p>", [1_600_000m]);
        Assert.Equal((47_990m, 1_600_000m, 34m), (amazon!.MedianEmployeePay, amazon.CeoPay, amazon.Ratio));
        // Smaller reporting companies say they're exempt.
        Assert.Null(PayRatioReader.Read("<p>As a smaller reporting company we are not required to provide pay ratio disclosure.</p>"));
    }

    [Fact]
    public void Uses_the_summary_compensation_table_when_the_disclosure_omits_the_CEOs_figure()
    {
        const string html = "<p>CEO Pay Ratio. The median of the annual total compensation of our employees was $65,293. The ratio of our CEO's pay to the median was 29:1.</p>";

        Assert.Null(PayRatioReader.Read(html));
        var found = PayRatioReader.Read(html, [1_893_500m]);
        Assert.NotNull(found);
        Assert.Equal(1_893_500m, found.CeoPay);
    }

    [Theory]
    [InlineData("Sr. Software Engineer", "senior software engineer")]
    [InlineData("SENIOR SOFTWARE ENGINEER (JR12345)", "senior software engineer")]
    [InlineData("Software Engineer II", "software engineer ii")]
    [InlineData("Software Engineer 2", "software engineer ii")]
    [InlineData("Data Scientist - 60123", "data scientist")]
    [InlineData("Mgr, Product Management", "manager product management")]
    public void Groups_job_titles(string title, string key) => Assert.Equal(key, JobTitles.Key(title));

    [Fact]
    public void Shows_a_titles_most_common_spelling()
    {
        Assert.Equal("Software Engineer", JobTitles.Display(["Software Engineer", "SOFTWARE ENGINEER", "Software Engineer"]));
        Assert.Equal("Software Engineer II", JobTitles.Display(["SOFTWARE ENGINEER II"]));
        // The employer's internal codes aren't part of the title.
        Assert.Equal("Software Engineer III", JobTitles.Display(["Software Engineer III (20831.45)", "Software Engineer III (20831.45)", "Software Engineer III"]));
        Assert.Equal("Data Scientist", JobTitles.Display(["Data Scientist - JR60123"]));
        // Nor is the place the ad is for.
        Assert.Equal("Software Developer Intern", JobTitles.Display(["Software Developer Intern - McLean, VA"]));
        Assert.Equal(JobTitles.Key("Data Analyst"), JobTitles.Key("Data Analyst, Remote"));    }

    [Theory]
    [InlineData("The Goldman Sachs Group, Inc.", "goldman sachs")]
    [InlineData("Goldman Sachs & Co. LLC", "goldman sachs")]
    [InlineData("Amazon.com Services LLC", "amazon com services")]
    [InlineData("JPMorgan Chase Bank, N.A.", "jpmorgan chase bank")]
    [InlineData("Visa U.S.A. Inc.", "visa")]
    [InlineData("AT&T Services, Inc.", "att services")]
    [InlineData("Wells Fargo & Company/Mn", "wells fargo")]
    [InlineData(@"US Bancorp \De\", "us bancorp")]
    [InlineData("Abercrombie & Fitch Co /De/", "abercrombie and fitch")]
    public void Reduces_employer_names_to_their_core(string name, string key) => Assert.Equal(key, EmployerMatcher.NameKey(name));

    [Fact]
    public void Matches_employers_by_tax_id_name_subsidiary_and_alias()
    {
        var m = new EmployerMatcher();
        m.AddCompany("AMZN", "Amazon Com Inc", "911646860");
        m.AddCompany("JPM", "JPMorgan Chase & Co", null);
        m.AddCompany("WMT", "Walmart Inc.", null);
        m.AddCompany("TGT", "Target Corp", null);
        m.AddCompany("GOOGL", "Alphabet Inc.", null);
        m.AddAlias("Google LLC", "GOOGL");

        Assert.Equal(("AMZN", "ein"), m.Match("Amazon.com Services LLC", "91-1646860"));
        Assert.Equal(("AMZN", "subsidiary"), m.Match("Amazon.com Services LLC", "12-3456789"));
        Assert.Equal(("JPM", "subsidiary"), m.Match("JPMorgan Chase Bank, N.A.", null));
        Assert.Equal(("WMT", "subsidiary"), m.Match("Walmart Associates, Inc.", null));
        Assert.Equal(("GOOGL", "alias"), m.Match("Google LLC", null));
        // A one-word name only when the rest says it's part of the company.
        Assert.Null(m.Match("Target Media Partners LLC", null));
        Assert.Null(m.Match("Infosys Limited", null));
    }

    [Theory]
    [InlineData("120000", "Year", 120_000)]
    [InlineData("60.50", "Hour", 125_840)]
    [InlineData("48.08", "Hour", 100_006)]   // 100,006.40: whole dollars
    [InlineData("2,500", "Week", 130_000)]
    [InlineData("10000", "Month", 120_000)]
    [InlineData("5000", "Bi-Weekly", 130_000)]
    public void Turns_offered_rates_into_yearly_salaries(string rate, string unit, int yearly) =>
        Assert.Equal(yearly, SalaryRun.Yearly(rate, unit));

    [Fact]
    public void Works_out_percentiles()
    {
        decimal[] sorted = [100, 200, 300, 400, 500];
        Assert.Equal(200, SalaryRun.Percentile(sorted, 0.25));
        Assert.Equal(300, SalaryRun.Percentile(sorted, 0.5));
        Assert.Equal(400, SalaryRun.Percentile(sorted, 0.75));
        Assert.Equal(150, SalaryRun.Percentile([100, 200], 0.5));
    }
}
