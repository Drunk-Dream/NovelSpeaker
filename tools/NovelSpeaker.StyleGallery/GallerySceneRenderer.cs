using System.Windows;
using System.Windows.Media.Imaging;

namespace NovelSpeaker.StyleGallery;

public static class GallerySceneRenderer
{
    public static RenderTargetBitmap Render(FrameworkElement root, GallerySceneDefinition scene)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(scene);

        root.Width = scene.Width;
        root.Height = scene.Height;
        root.Measure(new Size(scene.Width, scene.Height));
        root.Arrange(new Rect(0, 0, scene.Width, scene.Height));
        root.ApplyTemplate();
        root.UpdateLayout();

        var pixels = (int)Math.Round(scene.Width * GalleryRenderSettings.Dpi / 96d);
        var bitmap = new RenderTargetBitmap(
            pixels,
            (int)Math.Round(scene.Height * GalleryRenderSettings.Dpi / 96d),
            GalleryRenderSettings.Dpi,
            GalleryRenderSettings.Dpi,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }
}
