using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCvWindowToolWpfDemo.Controls
{
    /// <summary>
    /// 表示数值输入框允许输入的数值类型。
    /// </summary>
    public enum NumericInputValueKind
    {
        Integer,
        Float
    }

    /// <summary>
    /// 提供支持上下步进和范围限制的数值输入控件。
    /// </summary>
    public partial class NumericInputBox : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value),
                typeof(double),
                typeof(NumericInputBox),
                new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(
                nameof(Minimum),
                typeof(double),
                typeof(NumericInputBox),
                new PropertyMetadata(0d, OnRangePropertyChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(
                nameof(Maximum),
                typeof(double),
                typeof(NumericInputBox),
                new PropertyMetadata(100d, OnRangePropertyChanged));

        public static readonly DependencyProperty IncrementProperty =
            DependencyProperty.Register(
                nameof(Increment),
                typeof(double),
                typeof(NumericInputBox),
                new PropertyMetadata(1d));

        public static readonly DependencyProperty ValueKindProperty =
            DependencyProperty.Register(
                nameof(ValueKind),
                typeof(NumericInputValueKind),
                typeof(NumericInputBox),
                new PropertyMetadata(NumericInputValueKind.Float, OnRangePropertyChanged));

        public static readonly RoutedEvent ValueChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(ValueChanged),
                RoutingStrategy.Bubble,
                typeof(RoutedEventHandler),
                typeof(NumericInputBox));

        private bool updatingText;

        /// <summary>
        /// 初始化数值输入控件。
        /// </summary>
        public NumericInputBox()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 当数值发生变化时触发。
        /// </summary>
        public event RoutedEventHandler ValueChanged
        {
            add { AddHandler(ValueChangedEvent, value); }
            remove { RemoveHandler(ValueChangedEvent, value); }
        }

        /// <summary>
        /// 获取或设置当前数值。
        /// </summary>
        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        /// <summary>
        /// 获取或设置允许的最小值。
        /// </summary>
        public double Minimum
        {
            get { return (double)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        /// <summary>
        /// 获取或设置允许的最大值。
        /// </summary>
        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        /// <summary>
        /// 获取或设置每次步进的数值。
        /// </summary>
        public double Increment
        {
            get { return (double)GetValue(IncrementProperty); }
            set { SetValue(IncrementProperty, value); }
        }

        /// <summary>
        /// 获取或设置当前数值类型。
        /// </summary>
        public NumericInputValueKind ValueKind
        {
            get { return (NumericInputValueKind)GetValue(ValueKindProperty); }
            set { SetValue(ValueKindProperty, value); }
        }

        /// <summary>
        /// 获取当前数值的整数形式。
        /// </summary>
        public int IntValue
        {
            get { return (int)Math.Round(Value); }
        }

        /// <summary>
        /// 将输入值限制在合法范围内。
        /// </summary>
        /// <param name="target">目标依赖对象。</param>
        /// <param name="baseValue">原始输入值。</param>
        /// <returns>修正后的数值。</returns>
        private static object CoerceValue(DependencyObject target, object baseValue)
        {
            NumericInputBox input = (NumericInputBox)target;
            double value = (double)baseValue;
            if (double.IsNaN(value) || double.IsInfinity(value)) value = input.Minimum;

            double minimum = Math.Min(input.Minimum, input.Maximum);
            double maximum = Math.Max(input.Minimum, input.Maximum);
            value = Math.Max(minimum, Math.Min(maximum, value));
            if (input.ValueKind == NumericInputValueKind.Integer) value = Math.Round(value);
            return value;
        }

        /// <summary>
        /// 处理Value依赖属性变化。
        /// </summary>
        /// <param name="target">目标依赖对象。</param>
        /// <param name="e">属性变化参数。</param>
        private static void OnValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            NumericInputBox input = (NumericInputBox)target;
            input.UpdateTextFromValue();
            input.RaiseEvent(new RoutedEventArgs(ValueChangedEvent, input));
        }

        /// <summary>
        /// 处理范围或数值类型变化。
        /// </summary>
        /// <param name="target">目标依赖对象。</param>
        /// <param name="e">属性变化参数。</param>
        private static void OnRangePropertyChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            NumericInputBox input = (NumericInputBox)target;
            input.CoerceValue(ValueProperty);
            input.UpdateTextFromValue();
        }

        /// <summary>
        /// 控件加载后同步文本显示。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">事件参数。</param>
        private void NumericInputBox_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateTextFromValue();
        }

        /// <summary>
        /// 使用鼠标滚轮调整当前数值。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">鼠标滚轮事件参数。</param>
        private void NumericInputBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            StepValue(e.Delta > 0 ? Increment : -Increment);
            e.Handled = true;
        }

        /// <summary>
        /// 处理增加按钮点击。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">事件参数。</param>
        private void IncreaseButton_Click(object sender, RoutedEventArgs e)
        {
            StepValue(Increment);
        }

        /// <summary>
        /// 处理减少按钮点击。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">事件参数。</param>
        private void DecreaseButton_Click(object sender, RoutedEventArgs e)
        {
            StepValue(-Increment);
        }

        /// <summary>
        /// 限制文本框只接收合法字符。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">文本输入参数。</param>
        private void ValueTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !CanAcceptText(e.Text);
        }

        /// <summary>
        /// 文本变化时尝试同步数值。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">文本变化参数。</param>
        private void ValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (updatingText) return;
            if (TryParseValue(ValueTextBox.Text, out double value))
            {
                Value = value;
            }
        }

        /// <summary>
        /// 文本框失焦时提交当前文本。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">事件参数。</param>
        private void ValueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitText();
        }

        /// <summary>
        /// 处理文本框键盘操作。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">键盘事件参数。</param>
        private void ValueTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitText();
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                StepValue(Increment);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                StepValue(-Increment);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 按指定步长调整数值。
        /// </summary>
        /// <param name="delta">数值变化量。</param>
        private void StepValue(double delta)
        {
            double step = delta;
            if (ValueKind == NumericInputValueKind.Integer)
            {
                step = Math.Sign(delta) * Math.Max(1d, Math.Round(Math.Abs(delta)));
            }
            else if (step == 0d)
            {
                step = 1d;
            }

            Value = Value + step;
        }

        /// <summary>
        /// 提交文本框当前值。
        /// </summary>
        private void CommitText()
        {
            if (TryParseValue(ValueTextBox.Text, out double value))
            {
                Value = value;
            }
            UpdateTextFromValue();
        }

        /// <summary>
        /// 根据当前数值刷新文本框显示。
        /// </summary>
        private void UpdateTextFromValue()
        {
            if (ValueTextBox == null) return;
            string text = ValueKind == NumericInputValueKind.Integer
                ? IntValue.ToString(CultureInfo.InvariantCulture)
                : Value.ToString("0.###", CultureInfo.InvariantCulture);

            if (ValueTextBox.Text == text) return;
            updatingText = true;
            ValueTextBox.Text = text;
            ValueTextBox.CaretIndex = ValueTextBox.Text.Length;
            updatingText = false;
        }

        /// <summary>
        /// 判断输入文本是否允许追加。
        /// </summary>
        /// <param name="text">待追加文本。</param>
        /// <returns>允许追加时返回true。</returns>
        private bool CanAcceptText(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            foreach (char ch in text)
            {
                if (char.IsDigit(ch)) continue;
                if (ch == '-' && Minimum < 0d) continue;
                if (ValueKind == NumericInputValueKind.Float && (ch == '.' || ch == ',')) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 尝试把文本解析为数值。
        /// </summary>
        /// <param name="text">待解析文本。</param>
        /// <param name="value">解析后的数值。</param>
        /// <returns>解析成功时返回true。</returns>
        private bool TryParseValue(string text, out double value)
        {
            if (ValueKind == NumericInputValueKind.Integer)
            {
                bool parsed = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue);
                value = intValue;
                return parsed;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
    }
}
