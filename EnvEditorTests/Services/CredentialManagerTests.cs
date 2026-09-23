using EnvEditor.Services;

namespace EnvEditorTests.Services;

public class CredentialManagerTests
{
    [Theory]
    [InlineData("https://github.com/foo/bar.git", "git:https://github.com")]
    [InlineData("https://github.com", "git:https://github.com")]
    [InlineData("https://gitlab.example.com:8443/group/proj", "git:https://gitlab.example.com:8443")]
    [InlineData("http://internal-git:8080/a/b", "git:http://internal-git:8080")]
    public void TargetForUrl_UsesSchemeAndAuthority(string repoUrl, string expected)
    {
        // Windows 凭据管理器里 git 的通用凭据按 authority 存，路径/端口之外的部分不参与
        Assert.Equal(expected, CredentialManager.TargetForUrl(repoUrl));
    }

    [Theory]
    [InlineData("not-a-url", "git:not-a-url")]
    [InlineData("not-a-url/", "git:not-a-url")]
    public void TargetForUrl_FallsBackToRawString(string repoUrl, string expected)
    {
        Assert.Equal(expected, CredentialManager.TargetForUrl(repoUrl));
    }
}