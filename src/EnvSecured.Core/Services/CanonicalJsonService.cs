using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace EnvSecured.Core.Services
{
    public sealed class CanonicalJsonService
    {
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public string Canonicalize(object value)
        {
            var parsed = serializer.DeserializeObject(serializer.Serialize(value));
            var output = new StringBuilder();
            Write(parsed, output);
            return output.ToString();
        }

        private void Write(object value, StringBuilder output)
        {
            if (value == null)
            {
                output.Append("null");
                return;
            }
            if (value is string text)
            {
                output.Append(serializer.Serialize(text));
                return;
            }
            if (value is bool boolean)
            {
                output.Append(boolean ? "true" : "false");
                return;
            }
            if (value is IDictionary<string, object> dictionary)
            {
                output.Append('{');
                var first = true;
                foreach (var pair in dictionary.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    if (!first) output.Append(',');
                    first = false;
                    output.Append(serializer.Serialize(pair.Key));
                    output.Append(':');
                    Write(pair.Value, output);
                }
                output.Append('}');
                return;
            }
            if (value is IEnumerable sequence)
            {
                output.Append('[');
                var first = true;
                foreach (var item in sequence)
                {
                    if (!first) output.Append(',');
                    first = false;
                    Write(item, output);
                }
                output.Append(']');
                return;
            }
            if (value is double doubleValue)
            {
                if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
                    throw new InvalidOperationException("Non-finite numbers cannot be canonicalized.");
                output.Append(doubleValue.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (value is float floatValue)
            {
                if (float.IsNaN(floatValue) || float.IsInfinity(floatValue))
                    throw new InvalidOperationException("Non-finite numbers cannot be canonicalized.");
                output.Append(floatValue.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (value is IFormattable formattable)
            {
                output.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            }
            throw new InvalidOperationException("Unsupported canonical JSON value: " + value.GetType().FullName);
        }
    }
}
