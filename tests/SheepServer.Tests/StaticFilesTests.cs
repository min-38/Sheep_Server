using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SheepServer.Tests;

// 서버가 클라 빌드 파일(wwwroot)을 /ws와 같은 주소로 내보내는지 봄 (기술 설계서 배포)
public class StaticFilesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    // 실제 wwwroot는 빌드 결과라 git에 없어서, 클라 빌드를 흉내 낸 폴더를 테스트마다 만듦
    private readonly string webRoot = Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public async Task Root_ServesIndexHtml()
    {
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html><title>서부 총잡이</title>");
        using var client = CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("서부 총잡이", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Asset_ServedWithJavaScriptType()
    {
        Directory.CreateDirectory(Path.Combine(webRoot, "assets"));
        File.WriteAllText(Path.Combine(webRoot, "assets", "index-abc123.js"), "console.log(1)");
        using var client = CreateClient();

        var response = await client.GetAsync("/assets/index-abc123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    public void Dispose()
    {
        Directory.Delete(webRoot, recursive: true);
    }

    private HttpClient CreateClient()
    {
        return factory.WithWebHostBuilder(b => b.UseWebRoot(webRoot)).CreateClient();
    }
}
