using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using Xunit;

namespace OpenUtau.UiTest {
    /// <summary>
    /// The phonemizer menu is three levels deep and its language submenu is taller than the window,
    /// so the Fluent menu scroll arrows appeared. Docking an arrow changes the viewport, which is one
    /// of the four inputs to whether that arrow appears, and hovering an item scrolls it back into
    /// view, so the list jumped by one arrow height over and over. The arrows are switched off; a menu
    /// that overflows still has to scroll.
    /// </summary>
    public class MenuScrollArrowTest {
        static void Click(Window window, Control control) {
            var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            HeadlessUi.Flush();
        }

        [Fact]
        public void OverflowingSubmenuScrollsWithoutArrows() => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            HeadlessUi.Errors.Clear();
            // Short enough that the language rows do not fit, which is when the arrows appeared.
            var window = new MainWindow { Width = 1280, Height = 300 };
            try {
                window.Show();
                window.InitProject();
                HeadlessUi.Flush();
                var newButton = window.GetVisualDescendants().OfType<Button>().First(b =>
                    b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == window.FindResource("welcome.new") as string));
                Click(window, newButton);

                var header = window.GetVisualDescendants().OfType<TrackHeader>().First();
                var viewModel = (TrackHeaderViewModel)header.DataContext!;
                viewModel.RefreshPhonemizers();
                var menu = (ContextMenu)header.Resources["PhonemizersMenuRes"]!;
                menu.Open(header);
                HeadlessUi.Flush();
                AssertNoScrollArrows(menu);

                var more = menu.GetVisualDescendants().OfType<MenuItem>().First(i => i.Items?.Count > 0);
                more.IsSubMenuOpen = true;
                HeadlessUi.Flush();
                var submenu = more.GetVisualDescendants().OfType<Popup>().First().Child!;
                AssertNoScrollArrows(submenu);

                var scroller = submenu.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "the submenu does not overflow, so this proves nothing");
                scroller.LineDown();
                HeadlessUi.Flush();
                Assert.True(scroller.Offset.Y > 0, "the submenu stopped scrolling");
            } finally {
                window.Close();
            }
        });

        static void AssertNoScrollArrows(Control popupContent) {
            var scroller = popupContent.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.Equal(ScrollBarVisibility.Hidden, scroller.VerticalScrollBarVisibility);
            Assert.DoesNotContain(scroller.GetVisualDescendants().OfType<RepeatButton>(), button => button.IsVisible);
        }
    }
}
