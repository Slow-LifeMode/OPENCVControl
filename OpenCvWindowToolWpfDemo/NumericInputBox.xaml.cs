using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCvWindowToolWpfDemo
{
    public enum NumericInputValueKind
    {
        Integer,
        Float
    }

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

        public NumericInputBox()
        {
            InitializeComponent();
        }

        public event RoutedEventHandler ValueChanged
        {
            add { AddHandler(ValueChangedEvent, value); }
            remove { RemoveHandler(ValueChangedEvent, value); }
        }

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public double Minimum
        {
            get { return (double)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public double Increment
        {
            get { return (double)GetValue(IncrementProperty); }
            set { SetValue(IncrementProperty, value); }
        }

        public NumericInputValueKind ValueKind
        {
            get { return (NumericInputValueKind)GetValue(ValueKindProperty); }
            set { SetValue(ValueKindProperty, value); }
        }

        public int IntValue
        {
            get { return (int)Math.Round(Value); }
        }

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

        private static void OnValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            NumericInputBox input = (NumericInputBox)target;
            input.UpdateTextFromValue();
            input.RaiseEvent(new RoutedEventArgs(ValueChangedEvent, input));
        }

        private static void OnRangePropertyChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            NumericInputBox input = (NumericInputBox)target;
            input.CoerceValue(ValueProperty);
            input.UpdateTextFromValue();
        }

        private void NumericInputBox_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateTextFromValue();
        }

        private void NumericInputBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            StepValue(e.Delta > 0 ? Increment : -Increment);
            e.Handled = true;
        }

        private void IncreaseButton_Click(object sender, RoutedEventArgs e)
        {
            StepValue(Increment);
        }

        private void DecreaseButton_Click(object sender, RoutedEventArgs e)
        {
            StepValue(-Increment);
        }

        private void ValueTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !CanAcceptText(e.Text);
        }

        private void ValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (updatingText) return;
            if (TryParseValue(ValueTextBox.Text, out double value))
            {
                Value = value;
            }
        }

        private void ValueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitText();
        }

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

        private void CommitText()
        {
            if (TryParseValue(ValueTextBox.Text, out double value))
            {
                Value = value;
            }
            UpdateTextFromValue();
        }

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
