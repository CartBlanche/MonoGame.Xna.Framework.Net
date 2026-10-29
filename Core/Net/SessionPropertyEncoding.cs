using System.Globalization;

namespace Microsoft.Xna.Framework.Net
{
    /// <summary>
    /// Turns session property values into text and back, for back-ends that store them as lobby
    /// attributes (which are strings). The type travels with the value, so a search for the integer 7
    /// does not match the text "7".
    /// </summary>
    public static class SessionPropertyEncoding
    {
        /// <summary>Encodes an int, bool or string (anything else as its text).</summary>
        public static string Encode(object value)
        {
            switch (value)
            {
                case int i: return "i:" + i.ToString(CultureInfo.InvariantCulture);
                case bool b: return b ? "b:1" : "b:0";
                case string s: return "s:" + s;
                default: return "s:" + (value?.ToString() ?? string.Empty);
            }
        }

        /// <summary>Decodes text made by <see cref="Encode"/>; false if it is not.</summary>
        public static bool TryDecode(string text, out object value)
        {
            value = null;
            if (text == null || text.Length < 2 || text[1] != ':')
                return false;

            string body = text.Substring(2);
            switch (text[0])
            {
                case 'i':
                    if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                        return false;
                    value = i;
                    return true;
                case 'b':
                    if (body != "0" && body != "1")
                        return false;
                    value = body == "1";
                    return true;
                case 's':
                    value = body;
                    return true;
                default:
                    return false;
            }
        }
    }
}
