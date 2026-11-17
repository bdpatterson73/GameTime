using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace GameTime.WinUI;

/// <summary>
/// Animation helpers built on the composition layer. Everything here animates
/// Translation/Scale/Rotation, which run on the compositor thread and never
/// trigger layout, so motion stays smooth while game logic runs on the UI thread.
/// </summary>
internal static class Motion
{
    private const string OpenAnimationKey = "GameHeaderIcon";
    private const string CloseAnimationKey = "GameHeaderIconBack";

    private static Compositor? _compositor;
    private static CompositionEasingFunction? _overshoot;
    private static bool? _enabled;

    /// <summary>False when the user has turned off animation effects in Windows settings.</summary>
    public static bool Enabled => _enabled ??= new UISettings().AnimationsEnabled;

    /// <summary>
    /// Moves an element to a position; animated on the compositor when ms &gt; 0.
    /// Translation always holds the destination, so hit-testing matches where the element is going.
    /// </summary>
    public static void MoveTo(UIElement element, Vector3 target, int ms,
        Vector3TransitionComponents components = Vector3TransitionComponents.X | Vector3TransitionComponents.Y | Vector3TransitionComponents.Z)
    {
        element.TranslationTransition = ms > 0 && Enabled
            ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(ms), Components = components }
            : null;
        element.Translation = target;
    }

    /// <summary>Keeps scale and rotation anchored to the element's center as it resizes.</summary>
    public static void KeepCentered(FrameworkElement element)
    {
        element.SizeChanged += (_, e) =>
            element.CenterPoint = new Vector3((float)e.NewSize.Width / 2, (float)e.NewSize.Height / 2, 0);
    }

    /// <summary>Springs an element in from nothing with a little overshoot.</summary>
    public static void SpringIn(UIElement element)
    {
        if (!Enabled) return;
        var spring = Compositor.CreateSpringVector3Animation();
        spring.Target = "Scale";
        spring.InitialValue = Vector3.Zero;
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = 0.5f;
        spring.Period = TimeSpan.FromMilliseconds(70);
        element.StartAnimation(spring);
    }

    /// <summary>
    /// Springs an element's scale toward a value from wherever it currently is, so rapid
    /// hover/press changes blend naturally. The element's Scale must only be driven through this.
    /// </summary>
    public static void SpringScale(UIElement element, float scale)
    {
        if (!Enabled) return;
        var spring = Compositor.CreateSpringVector3Animation();
        spring.Target = "Scale";
        spring.FinalValue = new Vector3(scale, scale, 1f);
        spring.DampingRatio = 0.6f;
        spring.Period = TimeSpan.FromMilliseconds(50);
        element.StartAnimation(spring);
    }

    /// <summary>Quick scale-up pop with overshoot, used when a cell or tile changes state.</summary>
    public static void Pop(UIElement element, float from = 0.5f, int ms = 180)
    {
        if (!Enabled) return;
        var compositor = Compositor;
        _overshoot ??= compositor.CreateCubicBezierEasingFunction(new Vector2(0.34f, 1.56f), new Vector2(0.64f, 1f));

        var pop = compositor.CreateVector3KeyFrameAnimation();
        pop.Target = "Scale";
        pop.InsertKeyFrame(0f, new Vector3(from, from, 1f));
        pop.InsertKeyFrame(1f, Vector3.One, _overshoot);
        pop.Duration = TimeSpan.FromMilliseconds(ms);
        element.StartAnimation(pop);
    }

    /// <summary>Registers the hub tile icon as the source of the hub-to-game connected animation.</summary>
    public static void PrepareHeaderAnimation(UIElement source) =>
        ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(OpenAnimationKey, source);

    /// <summary>Flies the hub tile icon into a game page's header, if a hub navigation prepared one.</summary>
    public static void PlayHeaderAnimation(UIElement destination) =>
        ConnectedAnimationService.GetForCurrentView().GetAnimation(OpenAnimationKey)?.TryStart(destination);

    /// <summary>Registers a game page's header icon as the source of the game-to-hub connected animation.</summary>
    public static void PrepareReturnAnimation(UIElement source)
    {
        var animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(CloseAnimationKey, source);
        animation.Configuration = new DirectConnectedAnimationConfiguration();
    }

    /// <summary>Flies a game page's header icon back onto its hub tile, if a back navigation prepared one.</summary>
    public static void PlayReturnAnimation(UIElement destination) =>
        ConnectedAnimationService.GetForCurrentView().GetAnimation(CloseAnimationKey)?.TryStart(destination);

    // ElementCompositionPreview.GetElementVisual is not allowed on elements that use facade
    // properties (CenterPoint, Translation, Scale), so take the compositor from the UI thread.
    private static Compositor Compositor =>
        _compositor ??= Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread();
}
