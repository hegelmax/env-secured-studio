using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using EnvSecured.Core.Models;

namespace EnvSecured.Core.Services
{
    public static class EncryptedEnvelopeDetector
    {
        public const string Format = "EnvSecured.EncryptedVault";
        public const string LegacyFormat = "EnvSecured.EncryptedProject.v1";

        public static bool TryRead(string json, JavaScriptSerializer serializer, out EncryptedProjectFile envelope)
        {
            envelope = null;
            try
            {
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null ||
                    !root.TryGetValue("Format", out var format) ||
                    !string.Equals(Convert.ToString(format), Format, StringComparison.Ordinal) &&
                    !string.Equals(Convert.ToString(format), LegacyFormat, StringComparison.Ordinal))
                {
                    return false;
                }

                var candidate = serializer.Deserialize<EncryptedProjectFile>(json);
                if (candidate == null)
                {
                    return false;
                }

                envelope = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
