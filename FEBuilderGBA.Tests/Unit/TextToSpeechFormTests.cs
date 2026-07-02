using System;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    public class TextToSpeechFormTests
    {
        [Fact]
        public void Stop_WhenSpeechWasNeverInitialized_DoesNotThrow()
        {
            Exception ex = Record.Exception(() => TextToSpeechForm.Stop());

            Assert.Null(ex);
        }

        [Fact]
        public void TextToSpeechFormOuterType_DoesNotReferenceSystemSpeech()
        {
            string root = FindRepoRoot();
            string textToSpeechForm = File.ReadAllText(Path.Combine(root, "FEBuilderGBA", "TextToSpeechForm.cs"));

            Assert.DoesNotContain("System.Speech", textToSpeechForm, StringComparison.Ordinal);
            Assert.DoesNotContain("SpeechSynthesizer", textToSpeechForm, StringComparison.Ordinal);
            Assert.DoesNotContain("SynthesizerState", textToSpeechForm, StringComparison.Ordinal);
            Assert.Contains("StopInitializedSpeech", textToSpeechForm, StringComparison.Ordinal);
        }

        [Fact]
        public void EnsureSpeechInitialized_WhenFlagWasStale_RechecksEngineBoundary()
        {
            int initializeCalls = 0;
            TextToSpeechForm.SetSpeechInitializedForTests(true);
            TextToSpeechForm.SetTryInitializeSpeechForTests((bool isMultibyte, out string errorMessage) =>
            {
                initializeCalls++;
                errorMessage = "";
                return true;
            });

            try
            {
                bool initialized = TextToSpeechForm.EnsureSpeechInitialized(false, out string errorMessage);

                Assert.True(initialized);
                Assert.Equal("", errorMessage);
                Assert.Equal(1, initializeCalls);
                Assert.True(TextToSpeechForm.IsSpeechInitializedForTests);
            }
            finally
            {
                TextToSpeechForm.ResetSpeechTestHooksForTests();
            }
        }

        [Fact]
        public void EnsureSpeechInitialized_WhenEngineBoundaryFails_ClearsInitializedFlag()
        {
            TextToSpeechForm.SetSpeechInitializedForTests(true);
            TextToSpeechForm.SetTryInitializeSpeechForTests((bool isMultibyte, out string errorMessage) =>
            {
                errorMessage = "speech unavailable";
                return false;
            });

            try
            {
                bool initialized = TextToSpeechForm.EnsureSpeechInitialized(false, out string errorMessage);

                Assert.False(initialized);
                Assert.Equal("speech unavailable", errorMessage);
                Assert.False(TextToSpeechForm.IsSpeechInitializedForTests);
            }
            finally
            {
                TextToSpeechForm.ResetSpeechTestHooksForTests();
            }
        }

        [Theory]
        [InlineData("Hello!", false, false, "Hello.")]
        [InlineData("Hello. World", true, false, "Hello.\r\n World")]
        [InlineData("「はい！」", false, true, "はい。")]
        [InlineData("はい。次", true, true, "はい。\r\n次")]
        public void TextJoinCopy_PreservesExistingPunctuationBehavior(string input, bool useSentenceLineBreak, bool isMultibyte, string expected)
        {
            string actual = TextToSpeechTextUtil.TextJoinCopy(input, useSentenceLineBreak, isMultibyte);

            Assert.Equal(expected, actual);
        }

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not find FEBuilderGBA.sln from test base directory.");
        }
    }
}
