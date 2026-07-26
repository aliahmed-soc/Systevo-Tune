using SystevoTune.Engine.Cleanup;
using SystevoTune.TestSupport;

namespace SystevoTune.Engine.Tests.Cleanup;

public class CleanupWhitelistTests
{
    private readonly FakeEnvironmentPaths _environment = new();

    // ---- the shipped file ----

    [Fact]
    public void The_shipped_whitelist_loads()
        => Assert.NotEmpty(CleanupWhitelist.Load().Groups);

    [Fact]
    public void The_shipped_whitelist_has_the_three_groups_doc_3_1_names()
    {
        var ids = CleanupWhitelist.Load().Groups.Select(group => group.Id).ToList();

        Assert.Contains("temp-files", ids);
        Assert.Contains("windows-update-cache", ids);
        Assert.Contains("recycle-bin", ids);
    }

    [Fact]
    public void Every_shipped_group_has_an_arabic_name()
        => Assert.All(CleanupWhitelist.Load().Groups, group => Assert.False(string.IsNullOrWhiteSpace(group.NameAr)));

    [Fact]
    public void Every_shipped_path_survives_the_safety_guard()
    {
        foreach (var group in CleanupWhitelist.Load().Groups)
        {
            foreach (var path in group.Paths)
            {
                CleanupWhitelist.Resolve(path, _environment);
            }
        }
    }

    // ---- tokens ----

    [Fact]
    public void The_user_temp_token_resolves()
        => Assert.Equal(_environment.UserTemp, CleanupWhitelist.Resolve("{USER_TEMP}", _environment));

    [Fact]
    public void The_windows_token_resolves()
        => Assert.Equal(@"C:\FakeWindows\Temp", CleanupWhitelist.Resolve(@"{WINDIR}\Temp", _environment));

    [Fact]
    public void The_system_drive_token_does_not_double_the_separator()
        => Assert.Equal(@"C:\$Recycle.Bin", CleanupWhitelist.Resolve(@"{SYSTEM_DRIVE}\$Recycle.Bin", _environment));

    [Fact]
    public void An_unknown_token_is_refused()
        => Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Resolve(@"{PROGRAM_FILES}\junk", _environment));

    [Fact]
    public void A_relative_path_is_refused()
        => Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Resolve(@"..\junk", _environment));

    // ---- the guard: rule 6 ----

    [Theory]
    [InlineData(@"C:\FakeUsers\tester\Documents")]
    [InlineData(@"C:\FakeUsers\tester\Documents\work")]
    [InlineData(@"C:\FakeUsers\tester\Desktop")]
    [InlineData(@"C:\FakeUsers\tester\Downloads\big")]
    [InlineData(@"C:\FakeUsers\tester\Pictures")]
    public void User_folders_are_refused_even_if_the_whitelist_names_them(string path)
    {
        var error = Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Resolve(path, _environment));

        Assert.Contains("never touch", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_that_merely_starts_like_documents_is_still_allowed()
        => Assert.Equal(
            @"C:\FakeUsers\tester\DocumentsOld",
            CleanupWhitelist.Resolve(@"C:\FakeUsers\tester\DocumentsOld", _environment));

    [Fact]
    public void A_whole_drive_is_refused()
        => Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Resolve(@"C:\", _environment));

    [Fact]
    public void The_user_profile_root_is_refused()
        => Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Resolve(@"C:\FakeUsers\tester", _environment));

    [Fact]
    public void The_windows_folder_itself_is_refused()
        => Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Resolve("{WINDIR}", _environment));

    // ---- malformed files ----

    [Fact]
    public void A_whitelist_with_no_groups_is_refused()
        => Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Parse("""{"version":1,"groups":[]}"""));

    [Fact]
    public void A_duplicate_group_id_is_refused()
    {
        const string json = """
            {"version":1,"groups":[
              {"id":"a","nameEn":"A","nameAr":"أ","paths":["{WINDIR}\\Temp"]},
              {"id":"a","nameEn":"B","nameAr":"ب","paths":["{WINDIR}\\Temp"]}]}
            """;

        var error = Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Parse(json));

        Assert.Contains("twice", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_with_no_paths_is_refused()
    {
        const string json = """{"version":1,"groups":[{"id":"a","nameEn":"A","nameAr":"أ","paths":[]}]}""";

        Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Parse(json));
    }

    [Fact]
    public void Malformed_json_is_refused_with_a_readable_message()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Parse("{ not json"));

        Assert.Contains("could not be read", error.Message, StringComparison.Ordinal);
    }
}
