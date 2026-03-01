using System;
using System.Collections.Generic;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    public class Batch9Tests
    {
        // ---- R (Core facade) ----

        [Fact]
        public void R_Translate_ReturnsString()
        {
            // R._() delegates to MyTranslateResource.str which returns the input
            // when no translation is loaded
            string result = R._("hello world");
            Assert.Equal("hello world", result);
        }

        [Fact]
        public void R_Translate_WithArgs()
        {
            string result = R._("value is {0}", 42);
            Assert.Contains("42", result);
        }

        [Fact]
        public void R_Error_ReturnsString()
        {
            var saved = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = System.IO.Path.GetTempPath();
                Log.NonWriteStringB.Clear();
                string result = R.Error("test error {0}", "details");
                Assert.Contains("details", result);
            }
            finally
            {
                CoreState.BaseDirectory = saved;
            }
        }

        [Fact]
        public void R_Notify_ReturnsString()
        {
            var saved = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = System.IO.Path.GetTempPath();
                Log.NonWriteStringB.Clear();
                string result = R.Notify("notification {0}", "info");
                Assert.Contains("info", result);
            }
            finally
            {
                CoreState.BaseDirectory = saved;
            }
        }

        [Fact]
        public void R_ShowStopError_DoesNotThrow()
        {
            var saved = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = System.IO.Path.GetTempPath();
                Log.NonWriteStringB.Clear();
                // In Core, ShowStopError just logs — should not throw
                R.ShowStopError("error message {0}", "arg1");
                R.ShowStopError("simple error");
                R.ShowStopError("exception error", new InvalidOperationException("test"));
            }
            finally
            {
                CoreState.BaseDirectory = saved;
            }
        }

        // ---- Log (Core) ----

        [Fact]
        public void Log_Error_AppendsToBuffer()
        {
            var saved = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = System.IO.Path.GetTempPath();
                // Clear any accumulated buffer from other tests
                Log.NonWriteStringB.Clear();
                int before = Log.NonWriteStringB.Length;
                Log.Error("test log entry");
                Assert.True(Log.NonWriteStringB.Length > before);
            }
            finally
            {
                CoreState.BaseDirectory = saved;
            }
        }

        [Fact]
        public void Log_Notify_AppendsToBuffer()
        {
            var saved = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = System.IO.Path.GetTempPath();
                // Clear any accumulated buffer from other tests
                Log.NonWriteStringB.Clear();
                int before = Log.NonWriteStringB.Length;
                Log.Notify("test notification");
                Assert.True(Log.NonWriteStringB.Length > before);
            }
            finally
            {
                CoreState.BaseDirectory = saved;
            }
        }

        [Fact]
        public void Log_TouchLogDirectory_DoesNotThrow()
        {
            var saved = CoreState.BaseDirectory;
            try
            {
                // Use temp directory to avoid side effects
                CoreState.BaseDirectory = System.IO.Path.GetTempPath();
                Log.TouchLogDirectory();
                // Just verify it doesn't throw
            }
            finally
            {
                CoreState.BaseDirectory = saved;
            }
        }

        // ---- Mod ----

        [Fact]
        public void Mod_DefaultState()
        {
            var mod = new Mod();
            // Mods list is null before Load() is called
            Assert.Null(mod.Mods);
        }

        [Fact]
        public void Mod_Load_WithNoROM_EmptyMods()
        {
            var savedRom = CoreState.ROM;
            var savedBase = CoreState.BaseDirectory;
            var savedLang = CoreState.Language;
            try
            {
                // Create a minimal ROM with version 0 (ROMFE0)
                var rom = new ROM();
                byte[] data = new byte[256];
                rom.LoadLow("test.gba", data, "NAZO");
                CoreState.ROM = rom;
                CoreState.BaseDirectory = System.IO.Path.GetTempPath();
                CoreState.Language = "en";

                // Verify ROM is properly set up before calling Mod.Load
                Assert.NotNull(CoreState.ROM);
                Assert.NotNull(CoreState.ROM.RomInfo);

                var mod = new Mod();
                mod.Load();
                Assert.NotNull(mod.Mods);
                Assert.Empty(mod.Mods);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBase;
                CoreState.Language = savedLang;
            }
        }

        [Fact]
        public void ModSt_HasFormAndParam()
        {
            var modSt = new Mod.ModSt();
            modSt.Form = "TestForm";
            modSt.Param = new List<Mod.ModTypeSt>();

            Assert.Equal("TestForm", modSt.Form);
            Assert.Empty(modSt.Param);
        }

        [Fact]
        public void ModTypeSt_Fields()
        {
            var mtype = new Mod.ModTypeSt();
            mtype.key = "testKey";
            mtype.type = "VALUE";
            mtype.value = "testValue";

            Assert.Equal("testKey", mtype.key);
            Assert.Equal("VALUE", mtype.type);
            Assert.Equal("testValue", mtype.value);
        }
    }
}
