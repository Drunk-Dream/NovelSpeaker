using System.Windows.Controls;
using NovelSpeaker.App.Features.Playback.Components;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class PlayerProgressInteractionControllerTests
{
    [Fact]
    public void Progress_tooltip_stays_open_for_mouse_and_keyboard_interactions()
    {
        WpfTestHost.RunInSta(() =>
        {
            var target = new FakeProgressTarget();
            var controller = new PlayerProgressInteractionController(
                () => target,
                () => CancellationToken.None);
            var mouseSlider = CreateSlider();

            controller.OnMouseEnter(mouseSlider);
            var mouseTooltip = Assert.IsType<ToolTip>(mouseSlider.ToolTip);
            Assert.True(mouseTooltip.IsOpen);

            controller.BeginMouse(mouseSlider);

            Assert.True(mouseTooltip.IsOpen);
            controller.OnMouseLeave(mouseSlider);
            Assert.True(mouseTooltip.IsOpen);
            controller.CommitMouseAsync(mouseSlider).GetAwaiter().GetResult();
            Assert.False(mouseTooltip.IsOpen);

            controller.OnMouseLeave(mouseSlider);
            Assert.False(mouseTooltip.IsOpen);

            var keyboardSlider = CreateSlider();
            controller.BeginKeyboard(keyboardSlider, System.Windows.Input.Key.Right);

            var keyboardTooltip = Assert.IsType<ToolTip>(keyboardSlider.ToolTip);
            Assert.True(keyboardTooltip.IsOpen);
            controller.CommitKeyboardAsync(keyboardSlider, System.Windows.Input.Key.Right)
                .GetAwaiter()
                .GetResult();
            Assert.False(keyboardTooltip.IsOpen);
        });
    }

    private static Slider CreateSlider()
    {
        return new Slider
        {
            Value = 2,
            ToolTip = new ToolTip
            {
                Content = "3 / 10",
                StaysOpen = true
            }
        };
    }

    private sealed class FakeProgressTarget : ISegmentProgressInteractionTarget
    {
        public bool IsSegmentProgressDragging { get; private set; }

        public void BeginSegmentProgressInteraction() => IsSegmentProgressDragging = true;

        public void PreviewSegmentProgress(double value)
        {
        }

        public Task CommitSegmentProgressAsync(double value, CancellationToken cancellationToken)
        {
            IsSegmentProgressDragging = false;
            return Task.CompletedTask;
        }
    }
}
