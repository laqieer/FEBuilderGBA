// SPDX-License-Identifier: GPL-3.0-or-later
// Contract tests for AndroidResourceNoticeCore (#1641): the canonical Android
// patch2 / FE-Repo "documented limitation" messages + the injectable platform seam.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class AndroidResourceNoticeCoreTests
    {
        [Fact]
        public void PatchMessage_IsNonEmpty_AndMentionsPatch2AndPlanEpic()
        {
            string msg = AndroidResourceNoticeCore.PatchLibraryUnavailableMessage;
            Assert.False(string.IsNullOrWhiteSpace(msg));
            Assert.Contains("patch2", msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Android", msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("#1070", msg); // points users at the on-device-delivery epic
        }

        [Fact]
        public void FERepoMessage_IsNonEmpty_AndMentionsFERepoAndPlanEpic()
        {
            string msg = AndroidResourceNoticeCore.FERepoUnavailableMessage;
            Assert.False(string.IsNullOrWhiteSpace(msg));
            Assert.Contains("FE-Repo", msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Android", msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("#1070", msg);
        }

        [Fact]
        public void IsResourceDeliverySupported_TracksTheInjectableSeam()
        {
            var saved = AndroidResourceNoticeCore.IsAndroidOverride;
            try
            {
                AndroidResourceNoticeCore.IsAndroidOverride = () => true;  // pretend Android
                Assert.False(AndroidResourceNoticeCore.IsResourceDeliverySupported);

                AndroidResourceNoticeCore.IsAndroidOverride = () => false; // pretend desktop
                Assert.True(AndroidResourceNoticeCore.IsResourceDeliverySupported);
            }
            finally
            {
                AndroidResourceNoticeCore.IsAndroidOverride = saved;
            }
        }

        [Fact]
        public void Default_OnDesktopTestRunner_IsSupported()
        {
            // The test runner is a desktop OS (Linux/macOS/Windows), never Android,
            // so the default seam must report resource delivery as supported.
            Assert.True(AndroidResourceNoticeCore.IsResourceDeliverySupported);
        }
    }
}
