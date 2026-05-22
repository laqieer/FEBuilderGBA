using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for <see cref="ConversationViewerTabViewModel"/>.
    /// Mixes pure projection tests (no ROM needed) with one integration test
    /// against a real ROM via <see cref="RomFixture"/>.
    /// </summary>
    [Collection("SharedState")]
    public class ConversationViewerTabViewModelTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public ConversationViewerTabViewModelTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // ====================================================================
        // Pure projection tests (no ROM, no portrait service).
        // ====================================================================

        [Fact]
        public void Project_EmptyScript_ProducesNoCards()
        {
            var vm = new ConversationViewerTabViewModel();
            vm.LoadFromDecodedText("");
            Assert.Empty(vm.Cards);
        }

        [Fact]
        public void Project_PureTextNoCodes_ProducesOneTextCard()
        {
            var vm = new ConversationViewerTabViewModel();
            vm.LoadFromDecodedText("Hello world!");
            Assert.Single(vm.Cards);
            Assert.Equal(ConversationCardKind.Text, vm.Cards[0].Kind);
            Assert.Contains("Hello world!", vm.Cards[0].Bubble);
        }

        [Fact]
        public void Project_DisplayWithFFFFSentinel_ProducesVisitorPlaceholder()
        {
            // @0008@0010@FFFF = display visitor sentinel at left-edge slot
            var vm = new ConversationViewerTabViewModel();
            vm.LoadFromDecodedText("@0008@0010@FFFF");
            Assert.Single(vm.Cards);
            ConversationCardViewModel card = vm.Cards[0];
            Assert.Equal(ConversationCardKind.Display, card.Kind);
            Assert.Equal("Visitor", card.SpeakerName);
            Assert.Null(card.SpeakerBitmap); // sentinel always renders without portrait
        }

        [Fact]
        public void Project_EmptySlotInSerif_ProducesEmptyLabel()
        {
            // No prior @0010 display, so the slot is empty (units[0] == 0).
            // Serif lines from an empty slot should render with "(empty)" speaker.
            var vm = new ConversationViewerTabViewModel();
            vm.LoadFromDecodedText("@0008Hello");
            Assert.Single(vm.Cards);
            ConversationCardViewModel card = vm.Cards[0];
            Assert.Equal(ConversationCardKind.Serif, card.Kind);
            Assert.Equal("(empty)", card.SpeakerName);
            Assert.Null(card.SpeakerBitmap);
        }

        [Fact]
        public void Project_LeftRightSide_ClassifiedByCode1()
        {
            // pos 0x8-0xA = left, 0xB-0xF = right (mirrors WinForms `pos <= 2`)
            var vm = new ConversationViewerTabViewModel();

            vm.LoadFromDecodedText("@0008Hello");
            Assert.Single(vm.Cards);
            Assert.True(vm.Cards[0].IsLeftSide);
            Assert.False(vm.Cards[0].IsRightSide);

            vm.LoadFromDecodedText("@000DHello");
            Assert.Single(vm.Cards);
            Assert.False(vm.Cards[0].IsLeftSide);
            Assert.True(vm.Cards[0].IsRightSide);
        }

        [Fact]
        public void Project_HideCommand_ProducesHideCard()
        {
            var vm = new ConversationViewerTabViewModel();
            vm.LoadFromDecodedText("@0008@0011");
            Assert.Single(vm.Cards);
            Assert.Equal(ConversationCardKind.Hide, vm.Cards[0].Kind);
        }

        // ====================================================================
        // Integration test with a real ROM.
        // ====================================================================

        [Fact]
        public void RealRom_LoadKnownChapterText_ProducesCards()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }
            // Ensure the SkiaSharp image service is wired so portrait rendering can run.
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();

            // Pick any text ID with non-empty content. Most chapter dialogue is
            // in the 0x100-0x900 range; start at 0x1 and find the first ID
            // whose decoded text contains at least one @00XX position code.
            uint pickedId = 0;
            for (uint id = 1; id < 0x900; id++)
            {
                string decoded = FETextDecode.Direct(id) ?? "";
                if (decoded.Contains("@000"))
                {
                    pickedId = id;
                    break;
                }
            }

            if (pickedId == 0)
            {
                _output.WriteLine("No dialogue-style text found in ROM; skipping.");
                return;
            }

            _output.WriteLine($"Loading text id 0x{pickedId:X4}");

            var vm = new ConversationViewerTabViewModel();
            vm.LoadConversation(pickedId);

            Assert.NotEmpty(vm.Cards);
            _output.WriteLine($"Produced {vm.Cards.Count} cards.");
            // At least one card should be a Display or Serif (the dialogue rows)
            bool hasDialogue = false;
            foreach (var c in vm.Cards)
            {
                if (c.Kind == ConversationCardKind.Display || c.Kind == ConversationCardKind.Serif)
                {
                    hasDialogue = true;
                    break;
                }
            }
            Assert.True(hasDialogue, "Expected at least one Display or Serif card in a chapter dialogue.");
        }
    }
}
