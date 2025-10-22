using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace redfish.Controls
{
    public partial class Switch : UserControl
    {
        public string OptionA
        {
            get { return (string)GetValue(OptionAProperty); }
            set { SetValue(OptionAProperty, value); }
        }
        public static readonly DependencyProperty OptionAProperty =
            DependencyProperty.Register("OptionA", typeof(string), typeof(Switch),
                new PropertyMetadata("A", OnOptionAChanged));

        public string OptionB
        {
            get { return (string)GetValue(OptionBProperty); }
            set { SetValue(OptionBProperty, value); }
        }
        public static readonly DependencyProperty OptionBProperty =
            DependencyProperty.Register("OptionB", typeof(string), typeof(Switch),
                new PropertyMetadata("B", OnOptionBChanged));

        public bool IsOptionA { get; set; } = true;

        public Switch()
        {
            DataContext = this;
            InitializeComponent();
            Loaded += Switch_Loaded;
        }

        private void Switch_Loaded(object sender, RoutedEventArgs e)
        {
            // Set initial state without animation
            SetStateImmediate(IsOptionA);
        }

        private static void OnOptionAChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (Switch)d;
            control.OptionALabel.Text = e.NewValue.ToString();
        }

        private static void OnOptionBChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (Switch)d;
            control.OptionBLabel.Text = e.NewValue.ToString();
        }

        public void SetState(bool isOptionA)
        {
            IsOptionA = isOptionA;
            AnimateSwitch();
            UpdateLabels();
        }

        private void SetStateImmediate(bool isOptionA)
        {
            IsOptionA = isOptionA;

            // Position circle immediately without animation
            var circleWidth = SlidingCircle.Width;
            var trackWidth = SwitchBackground.ActualWidth;
            var margin = 3.0;

            if (double.IsNaN(circleWidth) || trackWidth == 0)
            {
                // If dimensions aren't available yet, use estimated values
                circleWidth = ActualHeight - 6;
                trackWidth = ActualWidth;
            }

            var leftPosition = margin;
            var rightPosition = trackWidth - circleWidth - margin;

            SlidingCircle.Margin = new Thickness(isOptionA ? leftPosition : rightPosition, 0, 0, 0);

            UpdateLabels();
        }

        private void AnimateSwitch()
        {
            var circleWidth = SlidingCircle.ActualWidth;
            var trackWidth = SwitchBackground.ActualWidth;
            var margin = 3.0;

            var leftPosition = margin;
            var rightPosition = trackWidth - circleWidth - margin;

            var targetMargin = IsOptionA ?
                new Thickness(leftPosition, 0, 0, 0) :
                new Thickness(rightPosition, 0, 0, 0);

            var slideAnimation = new ThicknessAnimation(
                SlidingCircle.Margin,
                targetMargin,
                TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new QuadraticEase() { EasingMode = EasingMode.EaseOut }
            };

            SlidingCircle.BeginAnimation(MarginProperty, slideAnimation);
        }

        private void UpdateLabels()
        {
            // Show the selected option's text on the opposite (visible) side
            // When A is selected (circle on left), show A's text on the right side
            // When B is selected (circle on right), show B's text on the left side

            if (IsOptionA)
            {
                // A is selected - show A's text on the right, hide left
                OptionALabel.Opacity = 0.0;  // Hide A label on left
                OptionBLabel.Text = OptionA;  // Show A's text on right
                OptionBLabel.Opacity = 1.0;
            }
            else
            {
                // B is selected - show B's text on the left, hide right  
                OptionBLabel.Opacity = 0.0;  // Hide B label on right
                OptionALabel.Text = OptionB;  // Show B's text on left
                OptionALabel.Opacity = 1.0;
            }

            // Add glow effect to background when active
            SwitchBackground.Effect = new DropShadowEffect()
            {
                ShadowDepth = 0,
                Color = Colors.White,
                BlurRadius = 4,
                Opacity = 0.6
            };
        }

        private void Switch_MouseEnter(object sender, MouseEventArgs e)
        {
            var hoverAnimation = new DoubleAnimation(
                SwitchBackground.Opacity,
                1.0,
                TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase() { EasingMode = EasingMode.EaseOut }
            };
            SwitchBackground.BeginAnimation(OpacityProperty, hoverAnimation);
        }

        private void Switch_MouseLeave(object sender, MouseEventArgs e)
        {
            var leaveAnimation = new DoubleAnimation(
                SwitchBackground.Opacity,
                0.7,
                TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase() { EasingMode = EasingMode.EaseOut }
            };
            SwitchBackground.BeginAnimation(OpacityProperty, leaveAnimation);
        }

        public event RoutedEventHandler OptionChanged;

        private void Switch_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                SetState(!IsOptionA);
                OptionChanged?.Invoke(this, e);
            }
        }

        // Convenience properties to get the selected option
        public string SelectedOption => IsOptionA ? OptionA : OptionB;
        public int SelectedIndex => IsOptionA ? 0 : 1;
    }
}