using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer.Postings;

namespace CompanyPaisa.Core.Tests;

/// <summary>Job ads: finding a company's board, reading the pay range and places out of an ad, and summarising them.</summary>
public class JobPostingTests
{
    [Theory]
    [InlineData("The base pay range for this role is $120,000 - $160,000.", 120_000, 160_000)]
    [InlineData("<div class=\"pay-range\"><span>$85,680</span><span class=\"divider\">&mdash;</span><span>$126,000 USD</span></div>", 85_680, 126_000)]
    [InlineData("&lt;div class=&quot;pay-range&quot;&gt;&lt;span&gt;$114,600&lt;/span&gt;&lt;span&gt;&amp;mdash;&lt;/span&gt;&lt;span&gt;$172,000 USD&lt;/span&gt;&lt;/div&gt;", 114_600, 172_000)]
    [InlineData("The base salary range is 136,000 USD - 218,500 USD for Level 3, and 168,000 USD - 264,500 USD for Level 4.", 136_000, 218_500)]
    [InlineData("$211.4K – $290.6K • Offers Equity", 211_400, 290_600)]
    [InlineData("Pay: $19.37 - $32.50 per hour", 40_290, 67_600)]
    [InlineData("Hourly pay range: $25.00 to $30.00", 52_000, 62_400)]
    [InlineData("Salary between $95K and $120K plus bonus.", 95_000, 120_000)]
    public void Reads_the_advertised_pay_range(string text, int min, int max)
    {
        var pay = PayText.Read(text);
        Assert.NotNull(pay);
        Assert.Equal((min, max), ((int)pay.Min, (int)pay.Max));
    }

    [Theory]
    [InlineData("We automate how over $200B in annualized spend flows in and out of 70,000+ companies.")]
    [InlineData("A sign-on bonus of $5,000 - $10,000 may be offered.")]
    [InlineData("Raised $50 - $75 million from investors.")]
    [InlineData("Founded 2015 - 2016 in Denver.")]
    [InlineData("Salary: $40,000 - $400,000")]   // more than 4× apart: not one job's range
    public void Ignores_amounts_that_arent_a_pay_range(string text) => Assert.Null(PayText.Read(text));

    [Theory]
    [InlineData("https://job-boards.greenhouse.io/doordashusa/jobs/7858932", "greenhouse", "doordashusa", null)]
    [InlineData("https://boards.greenhouse.io/embed/job_board?for=Airbnb&b=https://careers.airbnb.com", "greenhouse", "airbnb", null)]
    [InlineData("https://jobs.lever.co/palantir/abc-123", "lever", "palantir", null)]
    [InlineData("https://jobs.ashbyhq.com/ramp", "ashby", "ramp", null)]
    [InlineData("https://jobs.smartrecruiters.com/Visa/74400012345-engineer", "smartrecruiters", "Visa", null)]
    [InlineData("https://nvidia.wd5.myworkdayjobs.com/en-US/NVIDIAExternalCareerSite/job/US-CA-Santa-Clara/Engineer_JR1", "workday", "nvidia/NVIDIAExternalCareerSite", "nvidia.wd5.myworkdayjobs.com")]
    [InlineData("https://nvidia.wd5.myworkdayjobs.com/NVIDIAExternalCareerSite", "workday", "nvidia/NVIDIAExternalCareerSite", "nvidia.wd5.myworkdayjobs.com")]
    public void Recognises_job_boards_from_links(string url, string system, string board, string? host) =>
        Assert.Equal(new JobBoard(system, board, host), JobBoards.FromUrl(url));

    [Fact]
    public void Finds_boards_a_careers_page_links_to_or_embeds()
    {
        const string html = """
            <a href="https://www.example.com/life">Life here</a>
            <a href="/search">Search open positions</a>
            <script src="https://boards.greenhouse.io/embed/job_board/js?for=example"></script>
            <script>var cfg = {"jobs":"https:\/\/example.wd1.myworkdayjobs.com\/External"}</script>
            """;
        Assert.Equal([new JobBoard("greenhouse", "example"), new JobBoard("workday", "example/External", "example.wd1.myworkdayjobs.com")],
            JobBoards.FindInHtml(html));
        Assert.Equal("https://www.example.com/search", Assert.Single(JobBoards.JobListLinks(html, new Uri("https://www.example.com/careers"))).AbsoluteUri);
    }

    [Theory]
    [InlineData("Santa Clara, CA", "Santa Clara", "CA")]
    [InlineData("US, CA, Santa Clara", "Santa Clara", "CA")]
    [InlineData("USA-TX-Austin", "Austin", "TX")]
    [InlineData("New York City, New York, United States", "New York", "NY")]
    [InlineData("New York, NY (HQ)", "New York", "NY")]
    public void Reads_US_places_from_ads(string location, string city, string state) =>
        Assert.Equal((city, state), Assert.Single(UsPlaces.Parse(location)));

    [Fact]
    public void Knows_which_ads_are_in_the_US()
    {
        Assert.Equal(2, UsPlaces.Parse("San Francisco, CA; New York, NY").Count);
        Assert.True(UsPlaces.IsUs("Remote - US"));
        Assert.True(UsPlaces.IsUs("United States"));
        Assert.False(UsPlaces.IsUs("Israel, Yokneam"));
        Assert.False(UsPlaces.IsUs("London, United Kingdom"));
        Assert.Empty(UsPlaces.Parse("Remote, US"));
        Assert.Empty(UsPlaces.Parse("US, OR, Remote"));
    }

    [Theory]
    [InlineData("DoorDash USA", "Doordash, Inc.", true)]
    [InlineData("Airbnb", "Airbnb, Inc.", true)]
    [InlineData("Robinhood", "Robinhood Markets, Inc.", true)]
    [InlineData("Apple Leisure Group", "Apple Inc.", false)]
    public void Keeps_a_guessed_board_only_when_it_names_the_company(string board, string company, bool same) =>
        Assert.Equal(same, PostingRun.SameCompany(board, company));

    [Theory]
    [InlineData("DoorDash USA", "Doordash, Inc.", true)]
    [InlineData("1stDibs.com", "1stdibs.com, Inc.", true)]
    [InlineData("Alliance", "Alliance Resource Partners LP", false)]   // a shorter name is only trusted under the company's own website name
    public void A_guessed_Greenhouse_board_needs_the_companys_name(string board, string company, bool same) =>
        Assert.Equal(same, PostingRun.SameName(board, company));

    [Fact]
    public void A_guessed_Lever_or_Ashby_board_needs_ads_that_name_the_company()
    {
        Assert.True(PostingRun.Mentions("""[{"descriptionPlain":"About Analog Devices: we build chips."}]""", "Analog Devices, Inc."));
        Assert.False(PostingRun.Mentions("""[{"descriptionPlain":"Analog is a seed-stage startup."}]""", "Analog Devices, Inc."));
    }

    [Fact]
    public void Summarises_ads_by_title_and_city()
    {
        static SeenPosting Ad(string id, string title, string where, int min, int max, int day) =>
            new("greenhouse", "acme", "ACME", new Posting(id, title, where, $"https://x/{id}", new PayRange(min, max)), new DateOnly(2026, 9, day), new DateOnly(2026, 9, day));
        var rows = PostingRun.Summarise(
        [
            Ad("1", "Software Engineer", "Austin, TX", 100_000, 140_000, 1),
            Ad("2", "SOFTWARE ENGINEER", "Austin, TX; Seattle, WA", 110_000, 150_000, 2),
            Ad("3", "Software Engineer", "Seattle, WA", 130_000, 170_000, 3)
        ], new UsPlaces());

        var all = rows.Single(r => r.City is null);
        Assert.Equal(("Software Engineer", 3, 100_000m, 110_000m, 130_000m, 150_000m, 170_000m, "https://x/3", JobSalary.JobAds),
            (all.Title, all.Filings, all.Min, all.Low, all.Median, all.High, all.Max, all.Url, all.Source));
        Assert.Equal(2, rows.Single(r => r.City == "Austin").Filings);
        Assert.Equal(2, rows.Single(r => r.City == "Seattle").Filings);
    }
}
