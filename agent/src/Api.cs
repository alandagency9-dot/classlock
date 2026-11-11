using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;

namespace ClassLock
{
    public class ApiException : Exception
    {
        public ApiException(string message) : base(message) { }
    }

    /// <summary>Calls to Supabase. Uses only the public anon key; the
    /// service-role key never leaves the teacher panel's server.</summary>
    public static class Api
    {
        static readonly HttpClient Http;

        static Api()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Http = new HttpClient();
            Http.Timeout = TimeSpan.FromSeconds(15);
        }

        static Dictionary<string, object> Post(string path, object body, string bearer)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, BuildConfig.SupabaseUrl.TrimEnd('/') + path);
            req.Headers.Add("apikey", BuildConfig.AnonKey);
            req.Headers.Add("Authorization", "Bearer " + (bearer ?? BuildConfig.AnonKey));
            req.Content = new StringContent(Json.Write(body), Encoding.UTF8, "application/json");

            HttpResponseMessage res;
            try
            {
                res = Http.SendAsync(req).Result;
            }
            catch (AggregateException e)
            {
                throw new ApiException("No connection to the ClassLock server. Check the internet. (" +
                    e.GetBaseException().Message + ")");
            }
            string text = res.Content.ReadAsStringAsync().Result;
            Dictionary<string, object> obj = null;
            try { obj = Json.ReadObject(text); }
            catch (Exception) { /* not an object */ }

            if (!res.IsSuccessStatusCode)
            {
                string msg = Json.Str(obj, "msg") ?? Json.Str(obj, "message") ?? Json.Str(obj, "error_description")
                    ?? ("Server error " + (int)res.StatusCode);
                throw new ApiException(msg);
            }
            if (obj == null) throw new ApiException("Unexpected answer from the server.");
            return obj;
        }

        /// <summary>Puts this laptop in the class with this code. No login needed.</summary>
        public static Dictionary<string, object> RegisterDevice(string joinCode, string machineId, string hostname, string secret)
        {
            var body = new Dictionary<string, object>();
            body["p_join_code"] = joinCode;
            body["p_machine_id"] = machineId;
            body["p_hostname"] = hostname;
            body["p_secret"] = secret;
            return Post("/rest/v1/rpc/register_device", body, null);
        }

        public static Dictionary<string, object> Heartbeat(string deviceId, string secret, bool isLocked)
        {
            var body = new Dictionary<string, object>();
            body["p_device_id"] = deviceId;
            body["p_secret"] = secret;
            body["p_is_locked"] = isLocked;
            body["p_version"] = BuildConfig.Version;
            return Post("/rest/v1/rpc/device_heartbeat", body, null);
        }
    }
}
