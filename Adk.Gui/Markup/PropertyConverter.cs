using System;
using System.Globalization;
using Adk.Gui.Styling;
using VRageMath;

namespace Adk.Gui.Markup
{
    public sealed class PropertyConverter
    {
        public object Convert(string value, Type targetType)
        {
            if (targetType == null)
                throw new ArgumentNullException(nameof(targetType));
            if (targetType == typeof(string))
                return value;
            if (targetType == typeof(bool))
                return bool.Parse(value);
            if (targetType == typeof(int))
                return int.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(float))
                return float.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(double))
                return double.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(Color))
                return ParseColor(value);
            if (targetType == typeof(BackgroundBrush))
                return new SolidColorBrush(ParseColor(value));
            if (targetType == typeof(Vector2))
                return ParseVector2(value);
            if (targetType == typeof(Vector4))
                return ParseVector4(value);
            try
            {
                return Enum.Parse(targetType, value, true);
            }
            catch (ArgumentException)
            {
                throw new NotSupportedException(
                    "No XML conversion is registered for " + targetType.FullName + ".");
            }
        }


        static Vector2 ParseVector2(string value)
        {
            float[] values = ParseFloats(value, 1, 2);
            if (values.Length == 1)
                return new Vector2(values[0], values[0]);
            return new Vector2(values[0], values[1]);
        }

        static Vector4 ParseVector4(string value)
        {
            float[] values = ParseFloats(value, 1, 4);
            if (values.Length == 1)
                return new Vector4(values[0]);
            if (values.Length == 2)
                return new Vector4(values[0], values[1], values[0], values[1]);
            if (values.Length != 4)
                throw new FormatException(
                    "Expected 1, 2, or 4 components in '" + value + "'.");
            return new Vector4(values[0], values[1], values[2], values[3]);
        }

        static Color ParseColor(string value)
        {
            if (!string.IsNullOrEmpty(value) && value[0] == '#')
            {
                string hex = value.Substring(1);
                if (hex.Length == 6)
                    return new Color(
                        byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                        byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                        byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
                if (hex.Length == 8)
                    return new Color(
                        byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                        byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber),
                        byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber),
                        byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber));
            }

            float[] components = ParseFloats(value, 3, 4);
            return new Color(
                (byte)components[0],
                (byte)components[1],
                (byte)components[2],
                components.Length == 4 ? (byte)components[3] : (byte)255);
        }

        static float[] ParseFloats(string value, int requiredCount)
        {
            return ParseFloats(value, requiredCount, requiredCount);
        }

        static float[] ParseFloats(string value, int minimumCount, int maximumCount)
        {
            string[] parts = (value ?? string.Empty).Split(
                new[] { ',', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < minimumCount || parts.Length > maximumCount)
                throw new FormatException("Unexpected component count in '" + value + "'.");
            var result = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = float.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            return result;
        }
    }
}
