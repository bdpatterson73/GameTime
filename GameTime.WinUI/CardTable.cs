using System.Numerics;
using GameTime.WinUI.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;

namespace GameTime.WinUI;

/// <summary>
/// Card visuals used by the card game pages: a decoded-once image cache and
/// factories for card and pile-placeholder views positioned through Translation.
/// </summary>
internal static class CardTable
{
    // Elevation drives the ThemeShadow: a card casts a deeper shadow while hovered or in flight.
    private const float RestElevation = 8f;
    private const float HoverElevation = 24f;
    private const float FlightElevation = 40f;
    private const int SettleMs = 140;

    private static readonly Dictionary<string, BitmapImage> Images = new();
    private static readonly ThemeShadow Shadow = new();

    public static BitmapImage BackImage => Load("back");

    public static BitmapImage FaceImage(Card card) => Load(card.ImageKey);

    private static BitmapImage Load(string key)
    {
        if (!Images.TryGetValue(key, out var image))
        {
            // Decode at display size; cards never render wider than ~140 DIPs.
            image = new BitmapImage
            {
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelWidth = 160,
                UriSource = new Uri($"ms-appx:///Assets/Cards/{key}.png")
            };
            Images[key] = image;
        }
        return image;
    }

    public static Border CreateCardView(Card card, double width, double height)
    {
        var view = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Colors.White),
            Child = new Image { Stretch = Stretch.Fill },
            Tag = card,
            Shadow = Shadow,
            ScaleTransition = Motion.Enabled ? HoverTransition() : null
        };
        Resize(view, width, height);
        return view;
    }

    public static Border CreatePlaceholder(Pile pile, double width, double height, string? glyph, double glyphScale, byte glyphAlpha)
    {
        var view = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            Tag = pile
        };
        if (glyph is not null)
            view.Child = new TextBlock
            {
                Text = glyph,
                FontSize = width * glyphScale,
                Foreground = new SolidColorBrush(Color.FromArgb(glyphAlpha, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        Resize(view, width, height);
        return view;
    }

    public static Vector3Transition HoverTransition() => new() { Duration = TimeSpan.FromMilliseconds(100) };

    public static void Resize(Border view, double width, double height)
    {
        view.Width = width;
        view.Height = height;
        view.CenterPoint = new Vector3((float)width / 2, (float)height / 2, 0);
    }

    /// <summary>
    /// Places a view on the board; slides there on the compositor when ms &gt; 0. A lifted card
    /// rises as it travels, and placing it again at rest eases it back down onto the table.
    /// </summary>
    public static void Place(Border view, Point position, int ms = 0, bool lifted = false)
    {
        float z = view.Shadow is null ? 0 : lifted ? FlightElevation : RestElevation;
        // Whole-pixel positions keep card art crisp once it comes to rest.
        var target = new Vector3(MathF.Round((float)position.X), MathF.Round((float)position.Y), z);
        var current = view.Translation;
        if (ms == 0 && current.X == target.X && current.Y == target.Y)
        {
            // Already there (or on its way): only the elevation may need to settle.
            if (current.Z != target.Z)
                Motion.MoveTo(view, target, SettleMs, Vector3TransitionComponents.Z);
            return;
        }
        Motion.MoveTo(view, target, ms);
    }

    /// <summary>Grows a card and raises it off the table while the pointer is over it.</summary>
    public static void SetHover(Border view, bool hovered, bool changeElevation = true)
    {
        view.Scale = hovered ? new Vector3(1.05f, 1.05f, 1f) : Vector3.One;
        if (!changeElevation) return;
        var at = view.Translation;
        Motion.MoveTo(view, new Vector3(at.X, at.Y, hovered ? HoverElevation : RestElevation), 100, Vector3TransitionComponents.Z);
    }

    public static void SetSelected(Border view, bool selected)
    {
        view.BorderBrush = selected ? new SolidColorBrush(Colors.Gold) : null;
        view.BorderThickness = new Thickness(selected ? 3 : 0);
    }

    /// <summary>True for a left mouse button, touch, or pen contact press.</summary>
    public static bool IsPrimaryPress(UIElement element, PointerRoutedEventArgs e) =>
        e.GetCurrentPoint(element).Properties.IsLeftButtonPressed;

    /// <summary>Scatters a card across the board with a spin for the win celebration.</summary>
    public static void Scatter(Border view, Point position, double degrees, int ms)
    {
        view.RotationTransition = Motion.Enabled ? new ScalarTransition { Duration = TimeSpan.FromMilliseconds(ms) } : null;
        view.Rotation = (float)degrees;
        Place(view, position, ms, lifted: true);
    }
}

/// <summary>Detects a second press on the same target within the double-click interval.</summary>
internal sealed class DoubleClickTracker
{
    private const long IntervalMs = 500;

    private object? _lastTarget;
    private long _lastTick;

    public bool IsDoubleClick(object target)
    {
        long now = Environment.TickCount64;
        bool isDouble = ReferenceEquals(_lastTarget, target) && now - _lastTick <= IntervalMs;
        _lastTarget = isDouble ? null : target;
        _lastTick = now;
        return isDouble;
    }
}
