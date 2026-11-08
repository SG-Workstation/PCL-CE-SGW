using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL;

/// <summary>
/// 连续调色板控件：饱和度/明度取色方块 + 色相条 + 不透明度，输出带 Alpha 的颜色。
/// </summary>
public partial class MyColorPicker
{
    private const double StandardSaturation = 0.80d;
    private const double StandardValue = 0.85d;
    private const double SvCursorSize = 12d;
    private const double HueCursorHeight = 5d;

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color?), typeof(MyColorPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedColorChanged));

    private readonly SolidColorBrush _hueBaseBrush = new(Colors.Red);
    private readonly SolidColorBrush _previewBrush = new(Colors.Transparent);

    private int _alpha = 100;
    private bool _hasColor = true;
    private int _hue;
    private double _huePrecise;
    private bool _isDraggingHue;
    private bool _isDraggingSv;
    private bool _isUpdating;
    private double _saturation = StandardSaturation;
    private double _value = StandardValue;

    public MyColorPicker()
    {
        InitializeComponent();
        RectHueBase.Fill = _hueBaseBrush;
        BorderPreview.Background = _previewBrush;

        // 初始为标准饱和度/明度的红色；构造期间同步界面但不触发事件
        _hue = 0;
        _huePrecise = 0d;
        _saturation = StandardSaturation;
        _value = StandardValue;
        _alpha = 100;
        _hasColor = true;

        _isUpdating = true;
        try
        {
            RefreshUiAndCommit(false, false);
        }
        finally
        {
            _isUpdating = false;
        }

        SliderAlpha.Change += SliderAlpha_Change;

        SvSurface.MouseLeftButtonDown += SvSurface_MouseLeftButtonDown;
        SvSurface.MouseMove += SvSurface_MouseMove;
        SvSurface.MouseLeftButtonUp += SvSurface_MouseLeftButtonUp;
        SvSurface.LostMouseCapture += SvSurface_LostMouseCapture;
        SvSurface.SizeChanged += SvSurface_SizeChanged;

        HueSurface.MouseLeftButtonDown += HueSurface_MouseLeftButtonDown;
        HueSurface.MouseMove += HueSurface_MouseMove;
        HueSurface.MouseLeftButtonUp += HueSurface_MouseLeftButtonUp;
        HueSurface.LostMouseCapture += HueSurface_LostMouseCapture;
        HueSurface.SizeChanged += HueSurface_SizeChanged;
    }

    /// <summary>
    /// 当前选中的颜色（包含 Alpha）。程序化赋值不会触发 <see cref="SelectedColorChanged" />。
    /// </summary>
    public Color? SelectedColor
    {
        get => (Color?)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>
    /// 用户交互导致颜色变化时触发。
    /// </summary>
    public event Action<Color>? SelectedColorChanged;

    /// <summary>
    /// 用户操作色相条导致色相变化时触发，参数范围为 0-359。
    /// </summary>
    public event Action<int>? SelectedHueChanged;

    /// <summary>
    /// 程序化选中指定色相，并恢复到标准饱和度/明度与 100% 不透明度。不触发事件。
    /// </summary>
    public void SetHue(int hue)
    {
        _hue = NormalizeHue(hue);
        _huePrecise = _hue;
        _saturation = StandardSaturation;
        _value = StandardValue;
        _alpha = 100;
        _hasColor = true;

        _isUpdating = true;
        try
        {
            RefreshUiAndCommit(false, false);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <summary>
    /// 程序化设置颜色，并同步取色光标与不透明度滑块。不触发事件。
    /// </summary>
    public void SetColor(Color color)
    {
        var (hue, saturation, value) = GetHsv(color);
        var alpha = (int)Math.Round(color.A / 255d * 100d, MidpointRounding.AwayFromZero);

        // 保留精确色相，避免 RGB→HSV→RGB 往返时因整数色相产生 1 级色差
        _huePrecise = hue;
        _hue = NormalizeHue((int)Math.Round(hue, MidpointRounding.AwayFromZero));
        _saturation = saturation;
        _value = value;
        _alpha = Math.Clamp(alpha, 0, 100);
        _hasColor = true;

        _isUpdating = true;
        try
        {
            RefreshUiAndCommit(false, false);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MyColorPicker picker || picker._isUpdating)
            return;

        if (e.NewValue is Color color)
            picker.SetColor(color);
        else
            picker.ClearColorDisplay();
    }

    private void SliderAlpha_Change(object sender, bool user)
    {
        if (_isUpdating)
            return;

        _alpha = Math.Clamp(SliderAlpha.Value, 0, 100);
        _hasColor = true;
        RefreshUiAndCommit(true, false);
    }

    private void SvSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isUpdating)
            return;

        _isDraggingSv = true;
        SvSurface.CaptureMouse();
        SetFromSvPoint(e.GetPosition(SvSurface));
        e.Handled = true;
    }

    private void SvSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingSv || e.LeftButton != MouseButtonState.Pressed)
            return;

        SetFromSvPoint(e.GetPosition(SvSurface));
        e.Handled = true;
    }

    private void SvSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingSv)
            return;

        _isDraggingSv = false;
        SvSurface.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void SvSurface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isDraggingSv = false;
    }

    private void SetFromSvPoint(Point point)
    {
        if (SvSurface.ActualWidth <= 0d || SvSurface.ActualHeight <= 0d)
            return;

        var saturation = Math.Clamp(point.X / SvSurface.ActualWidth, 0d, 1d);
        var value = 1d - Math.Clamp(point.Y / SvSurface.ActualHeight, 0d, 1d);

        if (_hasColor && Math.Abs(saturation - _saturation) < 1e-9 &&
            Math.Abs(value - _value) < 1e-9)
        {
            UpdateCursors();
            return;
        }

        _saturation = saturation;
        _value = value;
        _hasColor = true;
        RefreshUiAndCommit(true, false);
    }

    private void HueSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isUpdating)
            return;

        _isDraggingHue = true;
        HueSurface.CaptureMouse();
        SetFromHuePoint(e.GetPosition(HueSurface));
        e.Handled = true;
    }

    private void HueSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingHue || e.LeftButton != MouseButtonState.Pressed)
            return;

        SetFromHuePoint(e.GetPosition(HueSurface));
        e.Handled = true;
    }

    private void HueSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingHue)
            return;

        _isDraggingHue = false;
        HueSurface.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void HueSurface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isDraggingHue = false;
    }

    private void SetFromHuePoint(Point point)
    {
        if (HueSurface.ActualHeight <= 0d)
            return;

        var ratio = Math.Clamp(point.Y / HueSurface.ActualHeight, 0d, 1d);
        var hue = Math.Min(359, (int)Math.Floor(ratio * 360d));

        if (_hasColor && hue == _hue)
        {
            UpdateCursors();
            return;
        }

        _hue = hue;
        _huePrecise = hue;
        _hasColor = true;
        RefreshUiAndCommit(true, true);
    }

    private void SvSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SvSurface.Clip = new RectangleGeometry(new Rect(0d, 0d, e.NewSize.Width, e.NewSize.Height), 3d, 3d);
        UpdateCursors();
    }

    private void HueSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        HueSurface.Clip = new RectangleGeometry(new Rect(0d, 0d, e.NewSize.Width, e.NewSize.Height), 3d, 3d);
        UpdateCursors();
    }

    /// <summary>
    /// 将内部状态同步到界面与依赖属性，并按需触发交互事件。
    /// </summary>
    private void RefreshUiAndCommit(bool raiseColorEvent, bool raiseHueEvent)
    {
        var wasUpdating = _isUpdating;
        var color = default(Color);
        var colorChanged = false;

        _isUpdating = true;
        try
        {
            // 程序化同步滑块；此时 Change 事件会被 _isUpdating 拦下
            if (SliderAlpha.Value != _alpha)
                SliderAlpha.Value = _alpha;
            TextAlpha.Text = _alpha + "%";

            UpdateHueBase();
            UpdateCursors();

            color = CalculateColor();
            colorChanged = SelectedColor is not { } oldColor || oldColor != color;
            _previewBrush.Color = color;
            TextPreview.Text = FormatColor(color);

            if (colorChanged)
                SetCurrentValue(SelectedColorProperty, color);
        }
        finally
        {
            _isUpdating = wasUpdating;
        }

        // 嵌套调用（如 SetColor 中被依赖属性回调再次进入）不重复触发事件
        if (wasUpdating)
            return;

        if (raiseColorEvent && colorChanged)
            SelectedColorChanged?.Invoke(color);
        if (raiseHueEvent)
            SelectedHueChanged?.Invoke(_hue);
    }

    private void UpdateHueBase()
    {
        _hueBaseBrush.Color = HsvToColor(_huePrecise, 1d, 1d);
    }

    private void UpdateCursors()
    {
        var svWidth = SvSurface.ActualWidth;
        var svHeight = SvSurface.ActualHeight;
        if (svWidth > 0d && svHeight > 0d)
        {
            var left = Math.Clamp(_saturation * svWidth - SvCursorSize / 2d, 0d, svWidth - SvCursorSize);
            var top = Math.Clamp((1d - _value) * svHeight - SvCursorSize / 2d, 0d, svHeight - SvCursorSize);
            Canvas.SetLeft(SvCursor, left);
            Canvas.SetTop(SvCursor, top);
        }

        SvCursor.Visibility = _hasColor ? Visibility.Visible : Visibility.Collapsed;

        var hueHeight = HueSurface.ActualHeight;
        if (hueHeight > 0d)
        {
            var top = Math.Clamp(_huePrecise / 360d * hueHeight - HueCursorHeight / 2d, 0d,
                Math.Max(0d, hueHeight - HueCursorHeight));
            Canvas.SetTop(HueCursor, top);
        }

        HueCursor.Visibility = _hasColor ? Visibility.Visible : Visibility.Collapsed;
    }

    private Color CalculateColor()
    {
        var baseColor = HsvToColor(_huePrecise, _saturation, _value);
        var alpha = (byte)Math.Clamp((int)Math.Round(_alpha * 255d / 100d, MidpointRounding.AwayFromZero), 0, 255);
        return Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
    }

    private void ClearColorDisplay()
    {
        _isUpdating = true;
        try
        {
            // 保留内部色相状态，用户再次操作取色区时可以直接恢复
            _hasColor = false;
            UpdateCursors();
            _previewBrush.Color = Colors.Transparent;
            TextPreview.Text = "#00000000";
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private static int NormalizeHue(int hue)
    {
        return (hue % 360 + 360) % 360;
    }

    private static string FormatColor(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>
    /// HSV 转 RGB，色相单位为度（0-360）。
    /// </summary>
    private static Color HsvToColor(double hue, double saturation, double value)
    {
        hue = (hue % 360d + 360d) % 360d;
        saturation = Math.Clamp(saturation, 0d, 1d);
        value = Math.Clamp(value, 0d, 1d);

        var chroma = value * saturation;
        var section = hue / 60d;
        var x = chroma * (1d - Math.Abs(section % 2d - 1d));
        var m = value - chroma;

        var (r, g, b) = section switch
        {
            < 1d => (chroma, x, 0d),
            < 2d => (x, chroma, 0d),
            < 3d => (0d, chroma, x),
            < 4d => (0d, x, chroma),
            < 5d => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return Color.FromRgb(ToByte((r + m) * 255d), ToByte((g + m) * 255d), ToByte((b + m) * 255d));
    }

    /// <summary>
    /// RGB 转 HSV。灰度没有色相，保留当前色相，避免操作取色区时色相条跳动。
    /// </summary>
    private (double Hue, double Saturation, double Value) GetHsv(Color color)
    {
        var r = color.R;
        var g = color.G;
        var b = color.B;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var value = max / 255d;
        var saturation = max == 0 ? 0d : delta / (double)max;

        if (delta == 0)
            return (_huePrecise, saturation, value);

        double hue;
        if (max == r)
            hue = 60d * (((g - b) / (double)delta) % 6d);
        else if (max == g)
            hue = 60d * ((b - r) / (double)delta + 2d);
        else
            hue = 60d * ((r - g) / (double)delta + 4d);

        if (hue < 0d)
            hue += 360d;

        return (hue % 360d, saturation, value);
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    }
}
