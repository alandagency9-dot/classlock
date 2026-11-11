using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace ClassLock
{
    /// <summary>Small helpers over the JSON serializer built into .NET Framework.</summary>
    public static class Json
    {
        public static string Write(object value)
        {
            return new JavaScriptSerializer().Serialize(value);
        }

        public static Dictionary<string, object> ReadObject(string text)
        {
            var obj = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
            if (obj == null) throw new FormatException("Expected a JSON object.");
            return obj;
        }

        public static object Get(IDictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return null;
            return v;
        }

        public static string Str(IDictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static bool Bool(IDictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            return v is bool && (bool)v;
        }

        public static int Int(IDictionary<string, object> d, string key, int fallback)
        {
            object v = Get(d, key);
            if (v == null) return fallback;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch (FormatException) { return fallback; }
            catch (InvalidCastException) { return fallback; }
            catch (OverflowException) { return fallback; }
        }

        public static object[] Arr(IDictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            var arr = v as object[];
            if (arr != null) return arr;
            var list = v as ArrayList;
            return list != null ? list.ToArray() : new object[0];
        }

        /// <summary>Parses a Postgres timestamptz string to UTC. Null when missing or invalid.</summary>
        public static DateTime? Utc(IDictionary<string, object> d, string key)
        {
            string s = Str(d, key);
            if (string.IsNullOrEmpty(s)) return null;
            DateTime t;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t))
                return DateTime.SpecifyKind(t, DateTimeKind.Utc);
            return null;
        }
    }
}
