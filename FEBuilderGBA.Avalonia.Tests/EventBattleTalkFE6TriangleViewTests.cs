using System;
using System.Collections.Generic;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class EventBattleTalkFE6TriangleViewTests
    {
        const int SyntheticFe6RomSize = 0x800000;
        const uint TriangleBase = 0x666528u;
        const string TriangleCharacterLabelKey = "Character (Unit):";
        const string MainHelpText = "Main battle-dialogue rows are 12-byte attacker/defender/text/flag records triggered when those two units fight.";
        const string SecondaryHelpText = "FE6's boss-conversation table stores 16-byte generic boss lines: one triggering unit, a chapter/map id, text, flag, and an event pointer.";
        const string TriangleHelpText = "FE6's triangle-attack quotes use a direct 6-row 16-byte table: character, chapter/map id, text, and event/flag byte only. There is no event pointer.";

        static void WithSyntheticFE6(Action<ROM> body)
        {
            var prevRom = CoreState.ROM;
            var prevServices = CoreState.Services;
            var prevUndo = CoreState.Undo;
            try
            {
                var rom = new ROM();
                Assert.True(rom.LoadLow("synthetic-fe6-view.gba", new byte[SyntheticFe6RomSize], "AFEJ01"));
                for (uint i = 0; i < 6; i++)
                {
                    uint addr = TriangleBase + i * 16;
                    rom.Data[addr + 0] = (byte)(0x10 + i);
                    rom.Data[addr + 1] = (byte)i;
                    rom.write_u16(addr + 4, 0x500 + i);
                    rom.Data[addr + 8] = (byte)(0x40 + i);
                }
                CoreState.ROM = rom;
                CoreState.Services = new HeadlessAppServices();
                CoreState.Undo = new Undo();
                NameResolver.ClearCache();
                body(rom);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.Services = prevServices;
                CoreState.Undo = prevUndo;
                NameResolver.ClearCache();
            }
        }

        [AvaloniaFact]
        public void TriangleSelector_LoadsSixRowsAndAppliesFieldVisibility()
        {
            WithSyntheticFE6(_ =>
            {
                var view = new EventBattleTalkFE6View();
                Invoke(view, "LoadList");

                var combo = view.FindControl<ComboBox>("TableFilter");
                Assert.NotNull(combo);
                Assert.Equal(3, combo!.ItemCount);
                var third = Assert.IsType<ComboBoxItem>(combo.Items[2]);
                var thirdText = Assert.IsType<TextBlock>(third.Content);
                Assert.Equal("Triangle attack (16-byte)", thirdText.Text);

                combo.SelectedIndex = 2;
                view.Measure(new Size(1200, 760));
                view.Arrange(new Rect(0, 0, 1200, 760));

                var entryList = view.FindControl<AddressListControl>("EntryList");
                Assert.NotNull(entryList);
                Assert.Equal(6, entryList!.ItemCount);

                Assert.Equal("Character:", view.FindControl<TextBlock>("AttackerLabel")!.Text);
                Assert.Contains("Chapter", view.FindControl<TextBlock>("DefenderLabel")!.Text ?? "");
                Assert.Equal("Event/Flag Byte:", view.FindControl<TextBlock>("AchievementFlagLabel")!.Text);
                Assert.Equal(TriangleHelpText, view.FindControl<TextBlock>("HelpTextLabel")!.Text);
                Assert.False(view.FindControl<TextBlock>("DefenderNameLabel")!.IsVisible);
                Assert.False(view.FindControl<TextBlock>("EventPointerLabel")!.IsVisible);
                Assert.False(view.FindControl<NumericUpDown>("EventPointerBox")!.IsVisible);
                Assert.False(view.FindControl<TextBlock>("Unknown02Label")!.IsVisible);
                Assert.False(view.FindControl<NumericUpDown>("Unknown02Box")!.IsVisible);
                Assert.Equal(255, view.FindControl<NumericUpDown>("AchievementFlagBox")!.Maximum);
            });
        }

        [AvaloniaFact]
        public void HelpText_SwitchesPerTable()
        {
            WithSyntheticFE6(_ =>
            {
                var view = new EventBattleTalkFE6View();
                Invoke(view, "LoadList");

                var combo = view.FindControl<ComboBox>("TableFilter");
                var help = view.FindControl<TextBlock>("HelpTextLabel");
                Assert.NotNull(combo);
                Assert.NotNull(help);

                Assert.Equal(MainHelpText, help!.Text);

                combo!.SelectedIndex = 1;
                Assert.Equal(SecondaryHelpText, help.Text);

                combo.SelectedIndex = 2;
                Assert.Equal(TriangleHelpText, help.Text);
            });
        }

        [AvaloniaFact]
        public void TriangleLocalization_UsesUnitSenseLabel_AndHelpTextTranslations()
        {
            string repoRoot = FindRepoRoot();
            string enPath = Path.Combine(repoRoot, "config", "translate", "en.txt");
            string jaPath = Path.Combine(repoRoot, "config", "translate", "ja.txt");
            string zhPath = Path.Combine(repoRoot, "config", "translate", "zh.txt");

            foreach (string path in new[] { enPath, jaPath, zhPath })
            {
                Assert.True(File.Exists(path), $"Missing translation file: {path}");
                var keys = ReadTranslationKeys(path);
                Assert.Contains(TriangleCharacterLabelKey, keys);
                Assert.Contains(MainHelpText, keys);
                Assert.Contains(SecondaryHelpText, keys);
                Assert.Contains(TriangleHelpText, keys);
            }

            AssertTranslation(jaPath, TriangleCharacterLabelKey, "ユニット:");
            AssertTranslation(jaPath, "Character:", "文字:");
            AssertTranslation(jaPath, TriangleHelpText, "FE6のトライアングルアタック台詞は、直接配置された6行固定の16バイトテーブルです。内容は [ユニット / 章・マップID / テキスト / イベント/フラグバイト] のみで、イベントポインタはありません。");
            AssertTranslatedTriangleView(
                jaPath,
                expectedLabel: "ユニット:",
                unexpectedGlyphLabel: "文字:",
                expectedHelp: "FE6のトライアングルアタック台詞は、直接配置された6行固定の16バイトテーブルです。内容は [ユニット / 章・マップID / テキスト / イベント/フラグバイト] のみで、イベントポインタはありません。");

            AssertTranslation(zhPath, TriangleCharacterLabelKey, "单位:");
            AssertTranslation(zhPath, "Character:", "字符:");
            AssertTranslation(zhPath, TriangleHelpText, "FE6的三角攻击台词使用直接写死的6行16字节表，内容只有 [单位 / 章节/地图ID / 文本 / 事件/标志字节]，没有事件指针。");
            AssertTranslatedTriangleView(
                zhPath,
                expectedLabel: "单位:",
                unexpectedGlyphLabel: "字符:",
                expectedHelp: "FE6的三角攻击台词使用直接写死的6行16字节表，内容只有 [单位 / 章节/地图ID / 文本 / 事件/标志字节]，没有事件指针。");
        }

        [AvaloniaFact]
        public void NavigateTo_TriangleAddress_SelectsThirdTableAndRow()
        {
            WithSyntheticFE6(_ =>
            {
                var view = new EventBattleTalkFE6View();
                Invoke(view, "LoadList");
                uint target = TriangleBase + 4 * 16;

                view.NavigateTo(target);

                var combo = view.FindControl<ComboBox>("TableFilter");
                Assert.NotNull(combo);
                Assert.Equal(2, combo!.SelectedIndex);
                var vm = Assert.IsType<EventBattleTalkFE6ViewModel>(view.DataViewModel);
                Assert.True(vm.IsTriangleAttackTable);
                Assert.Equal(target, vm.CurrentAddr);
            });
        }

        static void Invoke(EventBattleTalkFE6View view, string method)
        {
            var m = typeof(EventBattleTalkFE6View).GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            Assert.NotNull(m);
            m!.Invoke(view, Array.Empty<object?>());
        }

        static string FindRepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            throw new InvalidOperationException($"Could not locate FEBuilderGBA.sln starting from {start}");
        }

        static HashSet<string> ReadTranslationKeys(string path)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith(":", StringComparison.Ordinal))
                    keys.Add(line.Substring(1));
            }
            return keys;
        }

        static void AssertTranslation(string path, string key, string expected)
        {
            try
            {
                MyTranslateResource.Clear();
                MyTranslateResource.LoadResource(path);
                Assert.Equal(expected, R._(key));
            }
            finally
            {
                MyTranslateResource.Clear();
            }
        }

        static void AssertTranslatedTriangleView(string path, string expectedLabel, string unexpectedGlyphLabel, string expectedHelp)
        {
            try
            {
                MyTranslateResource.Clear();
                MyTranslateResource.LoadResource(path);

                WithSyntheticFE6(_ =>
                {
                    var view = new EventBattleTalkFE6View();
                    Invoke(view, "LoadList");
                    view.FindControl<ComboBox>("TableFilter")!.SelectedIndex = 2;

                    Assert.Equal(expectedLabel, view.FindControl<TextBlock>("AttackerLabel")!.Text);
                    Assert.NotEqual(unexpectedGlyphLabel, view.FindControl<TextBlock>("AttackerLabel")!.Text);
                    Assert.Equal(expectedHelp, view.FindControl<TextBlock>("HelpTextLabel")!.Text);
                });
            }
            finally
            {
                MyTranslateResource.Clear();
            }
        }
    }
}
