using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.UiTest {
    /// <summary>
    /// The wheel's default axis is a preference, and Shift always swaps to the other axis.
    /// Both are observed through the tracks view model's offsets, which are what the view draws.
    /// </summary>
    public class WheelScrollDirectionTest {
        [Fact]
        public void WheelFollowsPreferenceAndShiftSwaps() => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            HeadlessUi.Errors.Clear();
            var prefs = Preferences.Default;
            var (oldDirection, oldReduceAnimations) = (prefs.WheelScrollDirection, prefs.ReduceAnimations);
            // Without the glide the offsets land at once, so no frame timing is involved.
            prefs.ReduceAnimations = true;
            var window = new MainWindow { Width = 1280, Height = 800 };
            try {
                window.Show();
                window.InitProject();
                HeadlessUi.Flush();
                var newButton = window.GetVisualDescendants().OfType<Button>().First(b =>
                    b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == window.FindResource("welcome.new") as string));
                var at = newButton.TranslatePoint(new Point(newButton.Bounds.Width / 2, newButton.Bounds.Height / 2), window)!.Value;
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
                HeadlessUi.Flush();

                var tracks = ((MainWindowViewModel)window.DataContext!).TracksViewModel;
                // At the default zoom the whole project fits, leaving nothing to scroll horizontally.
                tracks.OnXZoomed(new Point(0.5, 0.5), 10);
                HeadlessUi.Flush();
                Assert.True(tracks.HScrollBarMax > 0, "no horizontal room to scroll");
                Assert.True(tracks.VScrollBarMax > 0, "no vertical room to scroll");

                var canvas = window.FindControl<Control>("partsCanvas")!;
                var center = canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window)!.Value;
                foreach (var (direction, shift, horizontal) in new[] {
                    (0, false, false),
                    (1, false, true),
                    (0, true, true),
                    (1, true, false),
                }) {
                    prefs.WheelScrollDirection = direction;
                    tracks.TickOffset = 0;
                    tracks.TrackOffset = 0;
                    HeadlessUi.Flush();
                    window.MouseWheel(center, new Vector(0, -3), shift ? RawInputModifiers.Shift : RawInputModifiers.None);
                    HeadlessUi.Flush();
                    Assert.True(tracks.TickOffset != 0 == horizontal,
                        $"direction={direction} shift={shift}: expected horizontal={horizontal}, got tickOffset={tracks.TickOffset}");
                    Assert.True(tracks.TrackOffset != 0 == !horizontal,
                        $"direction={direction} shift={shift}: expected vertical={!horizontal}, got trackOffset={tracks.TrackOffset}");
                }
                var errors = HeadlessUi.Errors.Snapshot();
                Assert.True(errors.Count == 0, "Errors reported:\n" + string.Join("\n", errors));
            } finally {
                (prefs.WheelScrollDirection, prefs.ReduceAnimations) = (oldDirection, oldReduceAnimations);
                HeadlessUi.Flush();
                HeadlessUi.SaveScreenshot(window, "WheelFollowsPreferenceAndShift");
                window.Close();
            }
        });

        [Fact]
        public void PreferencesDialogOffersBothDirections() => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            HeadlessUi.Errors.Clear();
            var prefs = Preferences.Default;
            var oldDirection = prefs.WheelScrollDirection;
            var dialog = new PreferencesDialog { Width = 900, Height = 700 };
            try {
                prefs.WheelScrollDirection = 1;
                dialog.DataContext = new PreferencesViewModel();
                dialog.Show();
                HeadlessUi.Flush();
                // The Editing page, which is where the option lives.
                var pages = dialog.FindControl<Carousel>("PageCarousel")!;
                pages.SelectedIndex = 3;
                HeadlessUi.Flush();
                var vertical = dialog.FindResource("prefs.editing.wheelscroll.vertical") as string;
                var horizontal = dialog.FindResource("prefs.editing.wheelscroll.horizontal") as string;
                Assert.False(string.IsNullOrEmpty(vertical));
                Assert.False(string.IsNullOrEmpty(horizontal));
                var box = pages.GetVisualDescendants().OfType<ComboBox>()
                    .SingleOrDefault(c => c.GetVisualDescendants().OfType<TextBlock>()
                        .Any(t => t.Text == vertical || t.Text == horizontal));
                Assert.NotNull(box);
                Assert.Equal(1, box.SelectedIndex);
                Assert.Equal(2, box.ItemCount);
            } finally {
                prefs.WheelScrollDirection = oldDirection;
                HeadlessUi.Flush();
                HeadlessUi.SaveScreenshot(dialog, "PreferencesDialogOffersBothDirections");
                dialog.Close();
            }
        });
    }
}