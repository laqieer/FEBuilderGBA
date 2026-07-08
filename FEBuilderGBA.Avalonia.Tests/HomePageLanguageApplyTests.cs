// SPDX-License-Identifier: GPL-3.0-or-later
// #1895 — the web-app home-page language switcher reuses OptionsViewModel.ApplyLanguage,
// factored out of Save(). These tests assert ApplyLanguage applies (and optionally
// persists) the language and raises LanguageChanged, and that a full Options Save()
// still persists BOTH a tool path AND the language in one call (guards the refactor —
// Save() must not lose its language behaviour or double-save).
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class HomePageLanguageApplyTests : IDisposable
{
    readonly Config? _savedConfig;
    readonly string? _savedBaseDir;
    readonly string? _savedLanguage;
    readonly string _savedGitPath;
    readonly string _baseDir;

    public HomePageLanguageApplyTests()
    {
        _savedConfig = CoreState.Config;
        _savedBaseDir = CoreState.BaseDirectory;
        _savedLanguage = CoreState.Language;
        _savedGitPath = CoreState.GitPath;

        // Isolated temp base dir OUTSIDE the repo (not a git repo) so Save()'s
        // ApplySubmoduleRemotes() cannot touch the real submodules.
        _baseDir = Path.Combine(Path.GetTempPath(), $"home_lang_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_baseDir, "config"));
        CoreState.BaseDirectory = _baseDir;
        CoreState.Config = Config.LoadOrCreate(Path.Combine(_baseDir, "config", "config.xml"));
    }

    public void Dispose()
    {
        CoreState.Config = _savedConfig;
        CoreState.BaseDirectory = _savedBaseDir;
        CoreState.Language = _savedLanguage;
        CoreState.GitPath = _savedGitPath;
        try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void ApplyLanguage_persist_sets_state_config_and_raises_event()
    {
        bool raised = false;
        Action handler = () => raised = true;
        CoreState.LanguageChanged += handler;
        try
        {
            OptionsViewModel.ApplyLanguage("ja");
        }
        finally
        {
            CoreState.LanguageChanged -= handler;
        }

        Assert.Equal("ja", CoreState.Language);
        Assert.True(raised);
        Assert.Equal("ja", CoreState.Config!["Language"]);
        Assert.Equal("ja", CoreState.Config!["func_lang"]); // WinForms back-compat key
    }

    [Fact]
    public void ApplyLanguage_no_persist_updates_state_but_not_config()
    {
        OptionsViewModel.ApplyLanguage("ja");                 // seed persisted "ja"
        OptionsViewModel.ApplyLanguage("en", persist: false); // in-memory switch only

        Assert.Equal("en", CoreState.Language);               // state switched
        Assert.Equal("ja", CoreState.Config!["Language"]);    // config NOT rewritten
    }

    [Fact]
    public void Full_Save_persists_both_a_tool_path_and_the_language()
    {
        var vm = new OptionsViewModel();
        vm.Load();
        vm.GitPath = "custom-git-1895";
        vm.Language = "ja \u2014 Japanese"; // "ja — Japanese" display string -> code "ja"

        vm.Save();

        // Reopen the config from disk to prove ONE Save() persisted BOTH the tool
        // path and the language (the ApplyLanguage refactor must not regress this).
        var reopened = Config.LoadOrCreate(Path.Combine(_baseDir, "config", "config.xml"));
        Assert.Equal("custom-git-1895", reopened["git_path"]);
        Assert.Equal("ja", reopened["Language"]);
        Assert.Equal("ja", reopened["func_lang"]);
    }
}
