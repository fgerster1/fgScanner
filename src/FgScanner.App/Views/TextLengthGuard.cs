using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FgScanner.App.Views;

/// <summary>Whether an edit may go in, and what to tell the operator when it may not.</summary>
public readonly record struct LengthDecision(bool Allowed, string? Message);

/// <summary>Raised when an edit is refused; the owning view shows the message on its status line.</summary>
public sealed class LengthRefusedEventArgs(RoutedEvent routedEvent, string message) : RoutedEventArgs(routedEvent)
{
    public string Message { get; } = message;
}

/// <summary>
/// Holds a TextBox to its field's length limit. Never TextBox.MaxLength: that cuts a long paste
/// silently, and on evidence a quietly truncated title is data loss nobody sees (SPEC-2026-002 §05 Q7).
/// Typing stops at the limit, and a paste that would go over is refused whole, with a message.
/// </summary>
public static class TextLengthGuard
{
    public static readonly DependencyProperty LimitProperty = DependencyProperty.RegisterAttached(
        "Limit", typeof(int?), typeof(TextLengthGuard), new PropertyMetadata(null, OnLimitChanged));

    public static readonly RoutedEvent RefusedEvent = EventManager.RegisterRoutedEvent(
        "Refused", RoutingStrategy.Bubble, typeof(EventHandler<LengthRefusedEventArgs>), typeof(TextLengthGuard));

    public static int? GetLimit(DependencyObject element) => (int?)element.GetValue(LimitProperty);

    public static void SetLimit(DependencyObject element, int? value) => element.SetValue(LimitProperty, value);

    public static LengthDecision Decide(string text, int selectionLength, string inserted, int? limit, bool pasted)
    {
        if (limit is not { } max)
        {
            return new(true, null);
        }

        var length = text.Length - selectionLength + inserted.Length;
        if (length <= max)
        {
            return new(true, null);
        }

        var allowed = max.ToString(CultureInfo.InvariantCulture);
        return new(false, pasted
            ? $"Nothing was pasted: the field would be {length.ToString(CultureInfo.InvariantCulture)} characters, and it allows {allowed}."
            : $"This field allows {allowed} characters.");
    }

    private static void OnLimitChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        // A grid column carries the limit for its cells; only a TextBox enforces it.
        if (element is not TextBox box)
        {
            return;
        }

        box.PreviewTextInput -= OnPreviewTextInput;
        DataObject.RemovePastingHandler(box, OnPasting);
        if (e.NewValue is int)
        {
            box.PreviewTextInput += OnPreviewTextInput;
            DataObject.AddPastingHandler(box, OnPasting);
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var box = (TextBox)sender;
        var decision = Decide(box.Text, box.SelectionLength, e.Text, GetLimit(box), pasted: false);
        if (!decision.Allowed)
        {
            e.Handled = true;
            Refuse(box, decision.Message);
        }
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        var box = (TextBox)sender;
        var incoming = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? "";
        var decision = Decide(box.Text, box.SelectionLength, incoming, GetLimit(box), pasted: true);
        if (!decision.Allowed)
        {
            e.CancelCommand();
            Refuse(box, decision.Message);
        }
    }

    private static void Refuse(TextBox box, string? message)
    {
        if (message is not null)
        {
            box.RaiseEvent(new LengthRefusedEventArgs(RefusedEvent, message));
        }
    }
}
