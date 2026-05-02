using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace JoinGameAfk.Validation
{
    public enum InputValidationState
    {
        Valid,
        Invalid
    }

    public enum NumericInputKind
    {
        Integer,
        Double
    }

    public sealed class NumericInputRule
    {
        internal NumericInputRule(
            TextBox textBox,
            string fieldName,
            NumericInputKind inputKind,
            double? minimum,
            double? maximum)
        {
            TextBox = textBox;
            FieldName = fieldName;
            InputKind = inputKind;
            Minimum = minimum;
            Maximum = maximum;
        }

        public TextBox TextBox { get; }

        public string FieldName { get; }

        public NumericInputKind InputKind { get; }

        public double? Minimum { get; }

        public double? Maximum { get; }

        public string ErrorMessage { get; private set; } = string.Empty;

        public bool Validate()
        {
            bool isValid = TryValidateText(TextBox.Text, out string errorMessage);
            ErrorMessage = errorMessage;
            InputValidator.SetValidationState(TextBox, isValid ? InputValidationState.Valid : InputValidationState.Invalid);

            return isValid;
        }

        public bool TryGetInt32(out int value)
        {
            value = 0;

            if (InputKind != NumericInputKind.Integer || !Validate())
                return false;

            return int.TryParse(TextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
        }

        public bool TryGetDouble(out double value)
        {
            value = 0;

            if (InputKind != NumericInputKind.Double || !Validate())
                return false;

            return double.TryParse(TextBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private bool TryValidateText(string? text, out string errorMessage)
        {
            string normalizedText = text?.Trim() ?? string.Empty;

            if (normalizedText.Length == 0)
            {
                errorMessage = $"{FieldName} is required.";
                return false;
            }

            bool parsed = InputKind == NumericInputKind.Integer
                ? int.TryParse(normalizedText, NumberStyles.Integer, CultureInfo.CurrentCulture, out int integerValue)
                    && IsWithinRange(integerValue)
                : double.TryParse(normalizedText, NumberStyles.Float, CultureInfo.CurrentCulture, out double doubleValue)
                    && IsWithinRange(doubleValue);

            errorMessage = parsed ? string.Empty : BuildRequirementMessage();
            return parsed;
        }

        private bool IsWithinRange(double value)
        {
            return (Minimum is null || value >= Minimum.Value)
                && (Maximum is null || value <= Maximum.Value);
        }

        private string BuildRequirementMessage()
        {
            string numberType = InputKind == NumericInputKind.Integer
                ? "a whole number"
                : "a number";

            if (Minimum is not null && Maximum is not null)
                return $"{FieldName} must be {numberType} between {FormatNumber(Minimum.Value)} and {FormatNumber(Maximum.Value)}.";

            if (Minimum is not null)
                return $"{FieldName} must be {numberType} of at least {FormatNumber(Minimum.Value)}.";

            if (Maximum is not null)
                return $"{FieldName} must be {numberType} no more than {FormatNumber(Maximum.Value)}.";

            return $"{FieldName} must be {numberType}.";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("G", CultureInfo.CurrentCulture);
        }
    }

    public static class InputValidator
    {
        public static readonly DependencyProperty ValidationStateProperty =
            DependencyProperty.RegisterAttached(
                "ValidationState",
                typeof(InputValidationState),
                typeof(InputValidator),
                new FrameworkPropertyMetadata(InputValidationState.Valid));

        public static InputValidationState GetValidationState(DependencyObject element)
        {
            return (InputValidationState)element.GetValue(ValidationStateProperty);
        }

        public static void SetValidationState(DependencyObject element, InputValidationState value)
        {
            element.SetValue(ValidationStateProperty, value);
        }

        public static NumericInputRule AttachInteger(TextBox textBox, string fieldName, int? minimum = null, int? maximum = null)
        {
            return AttachNumeric(textBox, fieldName, NumericInputKind.Integer, minimum, maximum);
        }

        public static NumericInputRule AttachDouble(TextBox textBox, string fieldName, double? minimum = null, double? maximum = null)
        {
            return AttachNumeric(textBox, fieldName, NumericInputKind.Double, minimum, maximum);
        }

        public static bool TryValidateAll(
            IEnumerable<NumericInputRule> rules,
            out NumericInputRule? invalidRule,
            out string errorMessage)
        {
            invalidRule = null;
            errorMessage = string.Empty;

            foreach (var rule in rules)
            {
                if (rule.Validate())
                    continue;

                invalidRule ??= rule;
                if (errorMessage.Length == 0)
                    errorMessage = rule.ErrorMessage;
            }

            return invalidRule is null;
        }

        private static NumericInputRule AttachNumeric(
            TextBox textBox,
            string fieldName,
            NumericInputKind inputKind,
            double? minimum,
            double? maximum)
        {
            var rule = new NumericInputRule(textBox, fieldName, inputKind, minimum, maximum);
            textBox.TextChanged += (_, _) => rule.Validate();
            rule.Validate();

            return rule;
        }
    }
}
