using UIKit;

namespace FEBuilderGBA.iOS
{
    /// <summary>
    /// iOS process entry point (#1859). Boots the UIKit application with
    /// <see cref="AppDelegate"/>, which in turn hosts the shared Avalonia
    /// <c>FEBuilderGBA.Avalonia.App</c> under the single-view lifetime.
    /// </summary>
    public static class Application
    {
        static void Main(string[] args)
        {
            UIApplication.Main(args, null, typeof(AppDelegate));
        }
    }
}
