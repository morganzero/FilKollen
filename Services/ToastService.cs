using System;
using System.Collections.Generic;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Serilog;

namespace FilKollen.Services
{
    public class ToastService
    {
        private readonly Window _parentWindow;
        private readonly StackPanel _toastContainer;
        private readonly ILogger _logger;
        private readonly Queue<ToastInfo> _toastQueue;
        private bool _isShowingToast;

        public ToastService(Window parent, ILogger? logger = null)
        {
            _parentWindow = parent;
            _logger = logger ?? Log.Logger ?? new LoggerConfiguration().WriteTo.Console().CreateLogger();
            _toastQueue = new Queue<ToastInfo>();
            _toastContainer = CreateToastContainer();
        }

        private StackPanel CreateToastContainer()
        {
            var container = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 80, 30, 0), // Under header
                MaxWidth = 400
            };

            // Lägg till i parent window's grid
            if (_parentWindow.Content is Grid mainGrid)
            {
                Grid.SetRowSpan(container, 3); // Spann över alla rader
                Grid.SetColumnSpan(container, 3); // Spann över alla kolumner
                mainGrid.Children.Add(container);
            }

            return container;
        }

        public void ShowToast(string message, ToastType type, int durationMs = 4000)
        {
            var toastInfo = new ToastInfo
            {
                Message = message,
                Type = type,
                Duration = durationMs
            };

            _toastQueue.Enqueue(toastInfo);
            ProcessToastQueue();
        }

        private async void ProcessToastQueue()
        {
            if (_isShowingToast || _toastQueue.Count == 0) return;

            _isShowingToast = true;
            var toastInfo = _toastQueue.Dequeue();

            try
            {
                await _parentWindow.Dispatcher.InvokeAsync(() =>
                {
                    var toast = CreateToast(toastInfo.Message, toastInfo.Type);
                    _toastContainer.Children.Add(toast);

                    AnimateToastIn(toast);

                    var timer = new Timer(toastInfo.Duration);
                    timer.Elapsed += (s, e) =>
                    {
                        _parentWindow.Dispatcher.InvokeAsync(() =>
                        {
                            AnimateToastOut(toast, () =>
                            {
                                _toastContainer.Children.Remove(toast);
                                _isShowingToast = false;
                                ProcessToastQueue(); // Process next toast
                            });
                        });
                        timer.Dispose();
                    };
                    timer.Start();
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Error showing toast: {ex.Message}");
                _isShowingToast = false;
                ProcessToastQueue();
            }
        }

        private Border CreateToast(string message, ToastType type)
        {
            var toast = new Border
            {
                Background = GetToastBackground(type),
                BorderBrush = GetToastBorder(type),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12),
                Margin = new Thickness(0, 0, 0, 8),
                MaxWidth = 380,
                Opacity = 0,
                RenderTransformOrigin = new Point(1, 0),
                RenderTransform = new TransformGroup
                {
                    Children = new TransformCollection
                    {
                        new TranslateTransform { X = 300 },
                        new ScaleTransform { ScaleX = 0.8, ScaleY = 0.8 }
                    }
                },
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 270,
                    ShadowDepth = 4,
                    BlurRadius = 12,
                    Opacity = 0.2
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Ikon
            var icon = new TextBlock
            {
                Text = GetToastIcon(type),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            // Meddelande
            var messageText = new TextBlock
            {
                Text = message,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = GetToastForeground(type),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                LineHeight = 20
            };
            Grid.SetColumn(messageText, 1);
            grid.Children.Add(messageText);

            // Stäng-knapp (optional)
            var closeButton = new Button
            {
                Content = "✕",
                Width = 20,
                Height = 20,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = GetToastForeground(type),
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                Opacity = 0.6,
                Margin = new Thickness(8, 0, 0, 0)
            };

            closeButton.Click += (s, e) =>
            {
                AnimateToastOut(toast, () => _toastContainer.Children.Remove(toast));
            };

            Grid.SetColumn(closeButton, 2);
            grid.Children.Add(closeButton);

            toast.Child = grid;
            return toast;
        }

        private void AnimateToastIn(Border toast)
        {
            var storyboard = new Storyboard();

            // Fade in
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, toast);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
            storyboard.Children.Add(fadeIn);

            // Slide in
            var slideIn = new DoubleAnimation
            {
                From = 300,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };
            Storyboard.SetTarget(slideIn, toast.RenderTransform);
            Storyboard.SetTargetProperty(slideIn, new PropertyPath("Children[0].X"));
            storyboard.Children.Add(slideIn);

            // Scale in
            var scaleInX = new DoubleAnimation
            {
                From = 0.8,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(350),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 }
            };
            Storyboard.SetTarget(scaleInX, toast.RenderTransform);
            Storyboard.SetTargetProperty(scaleInX, new PropertyPath("Children[1].ScaleX"));
            storyboard.Children.Add(scaleInX);

            var scaleInY = new DoubleAnimation
            {
                From = 0.8,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(350),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 }
            };
            Storyboard.SetTarget(scaleInY, toast.RenderTransform);
            Storyboard.SetTargetProperty(scaleInY, new PropertyPath("Children[1].ScaleY"));
            storyboard.Children.Add(scaleInY);

            storyboard.Begin();
        }

        private void AnimateToastOut(Border toast, Action onComplete)
        {
            var storyboard = new Storyboard();

            // Fade out
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(fadeOut, toast);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));
            storyboard.Children.Add(fadeOut);

            // Slide out
            var slideOut = new DoubleAnimation
            {
                From = 0,
                To = 300,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(slideOut, toast.RenderTransform);
            Storyboard.SetTargetProperty(slideOut, new PropertyPath("Children[0].X"));
            storyboard.Children.Add(slideOut);

            // Scale out
            var scaleOutX = new DoubleAnimation
            {
                From = 1,
                To = 0.9,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(scaleOutX, toast.RenderTransform);
            Storyboard.SetTargetProperty(scaleOutX, new PropertyPath("Children[1].ScaleX"));
            storyboard.Children.Add(scaleOutX);

            var scaleOutY = new DoubleAnimation
            {
                From = 1,
                To = 0.9,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(scaleOutY, toast.RenderTransform);
            Storyboard.SetTargetProperty(scaleOutY, new PropertyPath("Children[1].ScaleY"));
            storyboard.Children.Add(scaleOutY);

            storyboard.Completed += (s, e) => onComplete?.Invoke();
            storyboard.Begin();
        }

        private Brush GetToastBackground(ToastType type)
        {
            return type switch
            {
                ToastType.Success => Application.Current.FindResource("FK.Brush.Success") as Brush ?? new SolidColorBrush(Colors.Green),
                ToastType.Warning => Application.Current.FindResource("FK.Brush.Warning") as Brush ?? new SolidColorBrush(Colors.Orange),
                ToastType.Error => Application.Current.FindResource("FK.Brush.Danger") as Brush ?? new SolidColorBrush(Colors.Red),
                ToastType.Info => Application.Current.FindResource("FK.Brush.Primary") as Brush ?? new SolidColorBrush(Colors.Blue),
                _ => Application.Current.FindResource("FK.Brush.Surface") as Brush ?? new SolidColorBrush(Colors.White)
            };
        }

        private Brush GetToastBorder(ToastType type)
        {
            return type switch
            {
                ToastType.Success => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                ToastType.Warning => new SolidColorBrush(Color.FromRgb(251, 146, 60)),
                ToastType.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                ToastType.Info => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                _ => Application.Current.FindResource("FK.Brush.Border") as Brush ?? new SolidColorBrush(Colors.Gray)
            };
        }

        private Brush GetToastForeground(ToastType type)
        {
            return Brushes.White; // Alla toast-meddelanden har vit text för kontrast
        }

        private string GetToastIcon(ToastType type)
        {
            return type switch
            {
                ToastType.Success => "✅",
                ToastType.Warning => "⚠️",
                ToastType.Error => "❌",
                ToastType.Info => "ℹ️",
                _ => "📝"
            };
        }
    }

    public enum ToastType
    {
        Success,
        Warning,
        Error,
        Info
    }

    internal class ToastInfo
    {
        public string Message { get; set; } = string.Empty;
        public ToastType Type { get; set; }
        public int Duration { get; set; }
    }
}