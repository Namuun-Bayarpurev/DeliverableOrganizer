using DeliverableOrganizer.Services;

namespace DeliverableOrganizer.Tests;

public class PrUrlParserTests
{
    [Theory]
    [InlineData("https://dev.azure.com/Sysmac/Proj/_git/Repo/pullrequest/8586", "https://dev.azure.com/Sysmac", "Proj", "Repo", 8586)]
    [InlineData("https://dev.azure.com/Sysmac/Proj/_git/Repo/pullrequest/8586/", "https://dev.azure.com/Sysmac", "Proj", "Repo", 8586)]
    [InlineData("https://dev.azure.com/Sysmac/Proj/_git/Repo/pullrequest/8586?_a=files&path=/a.cs", "https://dev.azure.com/Sysmac", "Proj", "Repo", 8586)]
    [InlineData("  https://dev.azure.com/Sysmac/Proj/_git/Repo/pullrequest/8586#x  ", "https://dev.azure.com/Sysmac", "Proj", "Repo", 8586)]
    [InlineData("https://DEV.AZURE.COM/Sysmac/Proj/_GIT/Repo/PullRequest/1", "https://dev.azure.com/Sysmac", "Proj", "Repo", 1)]
    [InlineData("https://sysmac.visualstudio.com/Proj/_git/Repo/pullrequest/12", "https://sysmac.visualstudio.com", "Proj", "Repo", 12)]
    [InlineData("https://sysmac.visualstudio.com/DefaultCollection/Proj/_git/Repo/pullrequest/12", "https://sysmac.visualstudio.com/DefaultCollection", "Proj", "Repo", 12)]
    [InlineData("https://dev.azure.com/Sysmac/_git/Repo/pullrequest/3", "https://dev.azure.com/Sysmac", "Repo", "Repo", 3)]
    [InlineData("https://dev.azure.com/Sysmac/My%20Project/_git/My%20Repo/pullrequest/5", "https://dev.azure.com/Sysmac", "My Project", "My Repo", 5)]
    public void 正しい形式の_URL_を解析できる(string url, string baseUrl, string project, string repository, int id)
    {
        var pr = PrUrlParser.TryParse(url);

        Assert.NotNull(pr);
        Assert.Equal(baseUrl, pr.BaseUrl);
        Assert.Equal(project, pr.Project);
        Assert.Equal(repository, pr.Repository);
        Assert.Equal(id, pr.PullRequestId);
        Assert.Equal(url.Trim(), pr.SourceUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("8586")]
    [InlineData("dev.azure.com/Sysmac/Proj/_git/Repo/pullrequest/8586")]
    [InlineData("ftp://dev.azure.com/Sysmac/Proj/_git/Repo/pullrequest/8586")]
    [InlineData("https://github.com/org/repo/pull/1")]
    [InlineData("https://dev.azure.com/Sysmac/Proj/_git/Repo")]
    [InlineData("https://dev.azure.com/Sysmac/Proj/_git/Repo/pullrequests")]
    [InlineData("https://dev.azure.com/Sysmac/Proj/_git/Repo/pullrequest/abc")]
    [InlineData("https://dev.azure.com/Sysmac/Proj/_git/Repo/pullrequest/0")]
    [InlineData("https://dev.azure.com/Sysmac/Proj/_git/Repo/pullrequest/1/files")]
    [InlineData("https://dev.azure.com/Sysmac/A/B/_git/Repo/pullrequest/1")]
    [InlineData("https://dev.azure.com/Sysmac/Proj/_git/Repo/commit/abcdef")]
    public void 形式が正しくない_URL_は_null(string url)
    {
        Assert.Null(PrUrlParser.TryParse(url));
    }
}
