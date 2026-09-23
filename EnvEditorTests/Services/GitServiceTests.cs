using EnvEditor.Services;

namespace EnvEditorTests.Services;

public class GitServiceTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("team-a")]
    [InlineData("A_b-9")]
    [InlineData("0123456789")]
    [InlineData("团队一")]
    public void Sanitize_AcceptsAlphanumericDashUnderscore(string syncId)
    {
        // 同步 ID 只作为文件名片段使用，Unicode 字母合法（非只有 ASCII）
        Assert.Equal(syncId, GitService.Sanitize(syncId));
    }

    [Fact]
    public void Sanitize_AcceptsExactly64Chars()
    {
        var syncId = new string('a', 64);

        Assert.Equal(syncId, GitService.Sanitize(syncId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("with/slash")]
    [InlineData("..\\escape")]
    [InlineData("dot.name")]
    [InlineData("line\nbreak")]
    [InlineData("colon:name")]
    public void Sanitize_RejectsInvalidSyncId(string syncId)
    {
        // 同步 ID 直接参与拼路径 profiles/{id}.enc，必须挡住路径穿越与非法字符
        Assert.Throws<ArgumentException>(() => GitService.Sanitize(syncId));
    }

    [Fact]
    public void Sanitize_RejectsOver64Chars()
    {
        Assert.Throws<ArgumentException>(() => GitService.Sanitize(new string('a', 65)));
    }
}