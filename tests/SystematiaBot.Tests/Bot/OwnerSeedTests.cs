using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Tests.Bot;

public sealed class OwnerSeedTests : IDisposable
{
    private const long Owner = 42;
    private readonly string _dir = Directory.CreateTempSubdirectory("seed").FullName;
    private readonly FakeSettings _settings = new();
    private readonly IServiceProvider _services;

    public OwnerSeedTests() =>
        _services = new ServiceCollection().AddSingleton<IUserSettingsRepository>(_settings).BuildServiceProvider();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private Task Import() => OwnerSeed.ImportAsync(_services, Owner, _dir, NullLogger.Instance);

    [Fact]
    public async Task Profile_and_cv_are_imported_once_and_files_renamed()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, OwnerSeed.ProfileFile), "﻿  .NET developer, Lviv  \n");
        await File.WriteAllTextAsync(Path.Combine(_dir, OwnerSeed.CvFile), """{"name":"Test","projects":[{"id":"p","title":"P","bullets":[{"id":"b","text":"t"}]}]}""");

        await Import();

        _settings.Stored.Profile.ShouldBe(".NET developer, Lviv");
        _settings.Stored.CvJson.ShouldNotBeNull().ShouldContain("\"name\":\"Test\"");
        File.Exists(Path.Combine(_dir, OwnerSeed.ProfileFile + ".imported")).ShouldBeTrue();
        File.Exists(Path.Combine(_dir, OwnerSeed.CvFile)).ShouldBeFalse();
        _settings.Saves.ShouldBe(1);

        await Import();
        _settings.Saves.ShouldBe(1, "nothing to import on the next start");
    }

    [Fact]
    public async Task Broken_cv_is_left_in_place_and_does_not_touch_the_stored_one()
    {
        _settings.Stored.CvJson = "{\"name\":\"Old\"}";
        await File.WriteAllTextAsync(Path.Combine(_dir, OwnerSeed.CvFile), "{ not json");

        await Import();

        _settings.Stored.CvJson.ShouldBe("{\"name\":\"Old\"}");
        File.Exists(Path.Combine(_dir, OwnerSeed.CvFile)).ShouldBeTrue();
        _settings.Saves.ShouldBe(0);
    }

    private sealed class FakeSettings : IUserSettingsRepository
    {
        public UserSettings Stored { get; } = new() { ChatId = Owner };
        public int Saves { get; private set; }

        public Task<UserSettings> GetOrCreateAsync(long chatId, CancellationToken ct) => Task.FromResult(Stored);

        public Task SaveAsync(UserSettings settings, CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
