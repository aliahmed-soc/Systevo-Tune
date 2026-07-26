using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Safety;
using SystevoTune.TestSupport;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Registry;
using SystevoTune.Engine.Tweaks.Services;

namespace SystevoTune.Engine.Tests.Tweaks;

public class ServiceTweakTests : IDisposable
{
    private const string SysMain = """
        {"version":1,"services":[
          {"name":"SysMain","nameEn":"SysMain (Superfetch)","nameAr":"سيس مين","start":"Manual",
           "whyEn":"Prefetching helps little on an SSD."}]}
        """;

    private readonly TempLogDirectory _directory = new();
    private readonly FakeRegistryService _registry = new();
    private readonly TweakRunner _runner = new();

    public void Dispose() => _directory.Dispose();

    private ChangeLog NewLog()
        => new(_directory.Path, new FixedClock(new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero)));

    private static RegistryValueRef Start(string service) => ServiceWhitelist.StartValueRef(service);

    private ServicesModule Module(string json) => new(ServiceWhitelist.Parse(json), _registry);

    // ---- the shipped file ----

    [Fact]
    public void The_shipped_whitelist_is_empty_and_that_is_not_an_error()
        => Assert.Empty(ServiceWhitelist.Load().Services);

    [Fact]
    public void An_empty_whitelist_produces_no_tweaks()
        => Assert.Empty(new ServicesModule(ServiceWhitelist.Load(), _registry).CreateTweaks());

    // ---- the guard: golden rule 4 ----

    [Theory]
    [InlineData("WinDefend")]
    [InlineData("windefend")]
    [InlineData("MpsSvc")]
    [InlineData("Dhcp")]
    [InlineData("Dnscache")]
    [InlineData("Audiosrv")]
    [InlineData("Spooler")]
    [InlineData("ProfSvc")]
    public void A_forbidden_service_is_refused_even_if_the_file_names_it(string name)
    {
        var json = $$"""
            {"version":1,"services":[{"name":"{{name}}","nameEn":"X","nameAr":"س","start":"Disabled"}]}
            """;

        var error = Assert.Throws<InvalidOperationException>(() => ServiceWhitelist.Parse(json));

        Assert.Contains("may never change it", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Boot")]
    [InlineData("System")]
    public void A_driver_start_type_is_refused(string start)
    {
        var json = $$"""
            {"version":1,"services":[{"name":"SomeDriver","nameEn":"X","nameAr":"س","start":"{{start}}"}]}
            """;

        Assert.Throws<InvalidOperationException>(() => ServiceWhitelist.Parse(json));
    }

    [Fact]
    public void A_duplicate_service_is_refused()
    {
        const string json = """
            {"version":1,"services":[
              {"name":"SysMain","nameEn":"A","nameAr":"أ","start":"Manual"},
              {"name":"SysMain","nameEn":"B","nameAr":"ب","start":"Disabled"}]}
            """;

        Assert.Throws<InvalidOperationException>(() => ServiceWhitelist.Parse(json));
    }

    [Fact]
    public void A_service_name_that_is_a_path_is_refused()
        => Assert.Throws<InvalidOperationException>(() => ServiceWhitelist.StartValueRef(@"..\..\Control"));

    // ---- preview ----

    [Fact]
    public async Task A_service_that_is_not_installed_is_not_applicable()
    {
        var preview = await _runner.PreviewAsync(Module(SysMain).CreateTweaks());

        Assert.Equal(TweakStatus.NotApplicable, Assert.Single(preview.Plans).Status);
        Assert.Empty(_registry.Writes);
    }

    [Fact]
    public async Task A_service_already_on_the_target_start_type_has_nothing_to_do()
    {
        _registry.With(Start("SysMain"), RegistryValue.Dword((int)ServiceStartType.Manual));

        var preview = await _runner.PreviewAsync(Module(SysMain).CreateTweaks());

        Assert.Equal(TweakStatus.AlreadyApplied, Assert.Single(preview.Plans).Status);
    }

    [Fact]
    public async Task The_preview_reads_as_words_rather_than_digits()
    {
        _registry.With(Start("SysMain"), RegistryValue.Dword((int)ServiceStartType.Automatic));

        var preview = await _runner.PreviewAsync(Module(SysMain).CreateTweaks());

        Assert.Contains("Automatic to Manual", Assert.Single(preview.AllChanges).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retuning_a_service_needs_a_restart()
    {
        _registry.With(Start("SysMain"), RegistryValue.Dword((int)ServiceStartType.Automatic));

        Assert.True((await _runner.PreviewAsync(Module(SysMain).CreateTweaks())).RequiresRestart);
    }

    // ---- apply and undo ----

    [Fact]
    public async Task Applying_writes_only_the_start_value()
    {
        _registry.With(Start("SysMain"), RegistryValue.Dword((int)ServiceStartType.Automatic));

        await _runner.ApplyAsync(Module(SysMain).CreateTweaks(), NewLog().StartRun());

        Assert.All(_registry.Writes, write => Assert.EndsWith("::Start = Dword:3", write, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Undo_restores_the_exact_previous_start_type_not_a_default()
    {
        // Doc 7.3 spells this out: the previous value, not Automatic.
        _registry.With(Start("SysMain"), RegistryValue.Dword((int)ServiceStartType.Disabled));
        var log = NewLog();
        await _runner.ApplyAsync(Module(SysMain).CreateTweaks(), log.StartRun());

        var undo = await new UndoEngine(log, [new RegistryUndoHandler(_registry)]).UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal(RegistryValue.Dword((int)ServiceStartType.Disabled), _registry.GetValue(Start("SysMain")));
    }
}
