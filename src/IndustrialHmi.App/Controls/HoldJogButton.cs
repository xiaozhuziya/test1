using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IndustrialHmi.Core.ViewModels;
namespace IndustrialHmi.App.Controls;
// View-only pointer/keyboard adapter. Interlocks and writes remain in MachineController.
public sealed class HoldJogButton : Button {
    public static readonly DependencyProperty PositiveProperty = DependencyProperty.Register(nameof(Positive), typeof(bool), typeof(HoldJogButton), new PropertyMetadata(true));
    public bool Positive { get => (bool)GetValue(PositiveProperty); set => SetValue(PositiveProperty, value); }
    private bool held;
    public HoldJogButton() { Unloaded += async (_, _) => await ReleaseAsync(); IsEnabledChanged += async (_, _) => { if (!IsEnabled) await ReleaseAsync(); }; }
    private async Task PressAsync() { if (held || !IsEnabled) return; held = true; if (DataContext is MainViewModel vm) await vm.BeginJogAsync(Positive); }
    private async Task ReleaseAsync() { if (!held) return; held = false; if (IsMouseCaptured) ReleaseMouseCapture(); if (DataContext is MainViewModel vm) await vm.StopManualAsync(); }
    protected override async void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e) { e.Handled = true; Focus(); if (CaptureMouse()) await PressAsync(); }
    protected override async void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e) { e.Handled = true; await ReleaseAsync(); }
    protected override async void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); await ReleaseAsync(); }
    protected override async void OnPreviewKeyDown(KeyEventArgs e) { if (e.Key is Key.Space or Key.Enter) { e.Handled = true; if (!e.IsRepeat) await PressAsync(); } else base.OnPreviewKeyDown(e); }
    protected override async void OnPreviewKeyUp(KeyEventArgs e) { if (e.Key is Key.Space or Key.Enter) { e.Handled = true; await ReleaseAsync(); } else base.OnPreviewKeyUp(e); }
    protected override async void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); await ReleaseAsync(); }
}
