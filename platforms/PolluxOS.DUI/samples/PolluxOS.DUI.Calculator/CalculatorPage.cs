using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.PolluxOS.DUI.Interop;

namespace PolluxOS.DUI.Calculator;

/// <summary>
/// A four-function calculator built from ordinary MAUI controls: a <see cref="Grid"/> of
/// <see cref="Button"/>s plus a display <see cref="Label"/>. It exercises what the DUI
/// backend has to get right — grid layout, button text/colours, and clicks travelling
/// from a DUI control back into managed code.
///
/// The palette is deliberately light: the DUI window picks its own (platform) theme, and
/// the app paints the client area white so the calculator reads as a normal light app.
/// </summary>
public class CalculatorPage : ContentPage
{
    static readonly Color s_background = Colors.White;
    static readonly Color s_digitBackground = Color.FromArgb("#FFF2F2F7");
    static readonly Color s_operatorBackground = Color.FromArgb("#FFFF9F0A");
    static readonly Color s_actionBackground = Color.FromArgb("#FFD1D1D6");
    static readonly Color s_textColor = Color.FromArgb("#FF1C1C1E");

    readonly Label _display;
    readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    double _accumulator;
    string? _pendingOperator;
    bool _startNewEntry = true;

    public CalculatorPage()
    {
        _display = new Label
        {
            Text = "0",
            FontSize = 44,
            TextColor = s_textColor,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            AutomationId = "DisplayLabel",
        };

        var layout = new Grid
        {
            Padding = 20,
            RowSpacing = 10,
            ColumnSpacing = 10,
            BackgroundColor = s_background,
        };

        // A taller display row, so the digits have room of their own.
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(170) });
        for (var i = 0; i < 4; i++)
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });

        for (var i = 0; i < 4; i++)
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        layout.Add(_display, 0, 0);
        Grid.SetColumnSpan(_display, 4);

        AddButton(layout, "7", 1, 0, s_digitBackground);
        AddButton(layout, "8", 1, 1, s_digitBackground);
        AddButton(layout, "9", 1, 2, s_digitBackground);
        AddButton(layout, "÷", 1, 3, s_operatorBackground, Colors.White);

        AddButton(layout, "4", 2, 0, s_digitBackground);
        AddButton(layout, "5", 2, 1, s_digitBackground);
        AddButton(layout, "6", 2, 2, s_digitBackground);
        AddButton(layout, "×", 2, 3, s_operatorBackground, Colors.White);

        AddButton(layout, "1", 3, 0, s_digitBackground);
        AddButton(layout, "2", 3, 1, s_digitBackground);
        AddButton(layout, "3", 3, 2, s_digitBackground);
        AddButton(layout, "−", 3, 3, s_operatorBackground, Colors.White);

        AddButton(layout, "C", 4, 0, s_actionBackground, s_textColor, Clear);
        AddButton(layout, "0", 4, 1, s_digitBackground);
        AddButton(layout, "=", 4, 2, s_operatorBackground, Colors.White, Evaluate);
        AddButton(layout, "+", 4, 3, s_operatorBackground, Colors.White);

        Content = layout;
    }

    /// <summary>Displayed value, e.g. for tests.</summary>
    public string DisplayText => _display.Text ?? string.Empty;

    void AddButton(Grid grid, string text, int row, int column, Color background,
                   Color? textColor = null, Action? onClick = null)
    {
        var automationId = text switch
        {
            "+" => "BtnAdd",
            "−" => "BtnSubtract",
            "×" => "BtnMultiply",
            "÷" => "BtnDivide",
            "=" => "BtnEquals",
            "C" => "BtnClear",
            _ => $"Btn{text}",
        };

        var button = new Button
        {
            Text = text,
            FontSize = 24,
            BackgroundColor = background,
            TextColor = textColor ?? s_textColor,
            AutomationId = automationId,
        };

        button.Clicked += (_, _) =>
        {
            if (onClick is not null)
            {
                onClick();
                return;
            }

            if (text.Length == 1 && char.IsDigit(text[0]))
                AppendDigit(text[0]);
            else
                SetOperator(text);
        };

        _buttons[automationId] = button;

        grid.Add(button, column, row);
    }

    /// <summary>
    /// Presses a button through the *platform* (DUI) click notification, i.e. the same
    /// path a pointer click takes, rather than calling the handler directly. Used by the
    /// host's self-test to verify the native → managed wiring without input devices.
    /// </summary>
    public bool PressOnPlatform(string automationId)
    {
        if (!_buttons.TryGetValue(automationId, out var button))
            return false;

        return button.Handler?.PlatformView is DuiWidget widget && widget.Activate();
    }

    /// <summary>Bounds of a button's DUI control, in window coordinates.</summary>
    public bool TryGetButtonBounds(string automationId, out Rect bounds)
    {
        if (_buttons.TryGetValue(automationId, out var button)
            && button.Handler?.PlatformView is DuiWidget widget)
        {
            bounds = widget.GetBounds();
            return true;
        }

        bounds = Rect.Zero;
        return false;
    }

    void AppendDigit(char digit)
    {
        var current = _startNewEntry ? string.Empty : _display.Text ?? string.Empty;
        if (current == "0")
            current = string.Empty;

        current += digit;
        _display.Text = current.Length == 0 ? "0" : current;
        _startNewEntry = false;
    }

    void SetOperator(string op)
    {
        if (!_startNewEntry && _pendingOperator is not null)
            Evaluate();

        _accumulator = Parse(_display.Text);
        _pendingOperator = op;
        _startNewEntry = true;
    }

    void Evaluate()
    {
        if (_pendingOperator is null)
            return;

        var operand = Parse(_display.Text);
        var result = _pendingOperator switch
        {
            "+" => _accumulator + operand,
            "−" => _accumulator - operand,
            "×" => _accumulator * operand,
            "÷" => operand == 0 ? double.NaN : _accumulator / operand,
            _ => operand,
        };

        _display.Text = Format(result);
        _accumulator = result;
        _pendingOperator = null;
        _startNewEntry = true;
    }

    void Clear()
    {
        _display.Text = "0";
        _accumulator = 0;
        _pendingOperator = null;
        _startNewEntry = true;
    }

    static double Parse(string? text)
        => double.TryParse(text, out var value) ? value : 0;

    static string Format(double value)
        => double.IsNaN(value) ? "Error" : value.ToString("0.########");
}
