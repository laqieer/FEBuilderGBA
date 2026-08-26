using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class GitUtilLongPathTests
    {
        [Fact]
        public void BuildCloneArguments_WindowsPersistsLongPathsAndQuotesInputs()
        {
            string args = GitUtil.BuildCloneArguments(
                "https://example.invalid/content repo.git",
                @"C:\Program Files\FEBuilderGBA\resources\FE-Repo",
                isWindows: true);

            Assert.Equal(
                "clone --progress --depth=1 -c core.longpaths=true " +
                "\"https://example.invalid/content repo.git\" " +
                "\"C:\\Program Files\\FEBuilderGBA\\resources\\FE-Repo\"",
                args);
        }

        [Fact]
        public void BuildCloneArguments_NonWindowsPreservesExistingCommand()
        {
            string args = GitUtil.BuildCloneArguments(
                "https://example.invalid/content.git",
                "/opt/febuilder/resources/FE-Repo",
                isWindows: false);

            Assert.Equal(
                "clone --progress --depth=1 " +
                "\"https://example.invalid/content.git\" " +
                "\"/opt/febuilder/resources/FE-Repo\"",
                args);
        }

        [Fact]
        public void BuildUpdateArguments_WindowsScopesLongPathsToEveryCommand()
        {
            var commands = GitUtil.BuildUpdateArguments(
                "https://example.invalid/content repo.git",
                isWindows: true);

            Assert.Equal(new[]
            {
                "-c core.longpaths=true remote set-url origin " +
                "\"https://example.invalid/content repo.git\"",
                "-c core.longpaths=true fetch --progress --depth=1 origin",
                "-c core.longpaths=true reset --hard FETCH_HEAD",
            }, commands);
        }

        [Fact]
        public void BuildUpdateArguments_NonWindowsPreservesExistingCommands()
        {
            var commands = GitUtil.BuildUpdateArguments(
                "https://example.invalid/content.git",
                isWindows: false);

            Assert.Equal(new[]
            {
                "remote set-url origin \"https://example.invalid/content.git\"",
                "fetch --progress --depth=1 origin",
                "reset --hard FETCH_HEAD",
            }, commands);
        }

        [Fact]
        public void BuildUpdateArguments_WithoutRemoteOmitsSetUrl()
        {
            var commands = GitUtil.BuildUpdateArguments(null, isWindows: true);

            Assert.Equal(new[]
            {
                "-c core.longpaths=true fetch --progress --depth=1 origin",
                "-c core.longpaths=true reset --hard FETCH_HEAD",
            }, commands);
        }
    }
}
