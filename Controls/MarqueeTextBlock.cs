using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Size = System.Windows.Size;

namespace AirFoxBT.Controls;

public class MarqueeTextBlock : FrameworkElement
{
    private readonly TextBlock _textBlock;
    private readonly TranslateTransform _transform;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, e) => ((MarqueeTextBlock)d).OnTextPropertyChanged()));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((MarqueeTextBlock)d)._textBlock.Foreground = (Brush)e.NewValue));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, e) => ((MarqueeTextBlock)d)._textBlock.FontSize = (double)e.NewValue));

    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, e) => ((MarqueeTextBlock)d)._textBlock.FontWeight = (FontWeight)e.NewValue));

    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(new FontFamily("Segoe UI"), FrameworkPropertyMetadataOptions.AffectsMeasure, (d, e) => ((MarqueeTextBlock)d)._textBlock.FontFamily = (FontFamily)e.NewValue));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public MarqueeTextBlock()
    {
        ClipToBounds = true;
        _transform = new TranslateTransform();
        _textBlock = new TextBlock
        {
            RenderTransform = _transform,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        AddVisualChild(_textBlock);
        AddLogicalChild(_textBlock);

        Loaded += (s, e) => UpdateAnimation();
        SizeChanged += (s, e) => UpdateAnimation();
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _textBlock;

    private void OnTextPropertyChanged()
    {
        _textBlock.Text = Text;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Unconstrained measure so TextBlock calculates its full natural width
        _textBlock.Measure(new Size(double.PositiveInfinity, double.IsPositiveInfinity(availableSize.Height) ? 30 : availableSize.Height));

        double height = _textBlock.DesiredSize.Height;
        double width = double.IsPositiveInfinity(availableSize.Width) ? _textBlock.DesiredSize.Width : availableSize.Width;

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Arrange TextBlock with its FULL natural width so WPF renders every character
        double fullTextWidth = Math.Max(finalSize.Width, _textBlock.DesiredSize.Width);
        _textBlock.Arrange(new Rect(0, 0, fullTextWidth, finalSize.Height));

        UpdateAnimation();
        return finalSize;
    }

    private void UpdateAnimation()
    {
        if (!IsLoaded || ActualWidth <= 0) return;

        double textWidth = _textBlock.DesiredSize.Width;
        double availableWidth = ActualWidth;

        _transform.BeginAnimation(TranslateTransform.XProperty, null);
        _transform.X = 0;

        if (textWidth > availableWidth + 2)
        {
            double overflow = textWidth - availableWidth + 14; // Margin so trailing text is completely revealed
            double speed = 28.0; // px/sec - smooth reading pace
            double travelDuration = Math.Max(1.0, overflow / speed);
            double pauseDuration = 1.3; // Seconds to pause at start & end

            var anim = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                Duration = TimeSpan.FromSeconds((pauseDuration * 2) + (travelDuration * 2) + 0.5)
            };

            double t = 0;
            // 1. Initial pause at start so user can read beginning
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, TimeSpan.FromSeconds(0)));
            t += pauseDuration;
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(t)));

            // 2. Smoothly glide to end
            t += travelDuration;
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, TimeSpan.FromSeconds(t)));

            // 3. Pause at end so user can read trailing text
            t += pauseDuration;
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, TimeSpan.FromSeconds(t)));

            // 4. Smoothly glide back to start (Ping-Pong)
            t += travelDuration;
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(t)));

            // 5. Brief rest before next loop
            t += 0.5;
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(t)));

            _transform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }
}
