using CodeName399.Kite.API.Models;
using DatabaseRepository.Common.Utilities;
using System.Text;
using System.Text.Json;

namespace CodeName399.Kite.API.Extensions
{
    public static class HttpRequestMessageExtension
    {
        public static void AddAngelOneHeaders(
            this HttpRequestMessage httpRequestMessage,
            AngelOneConfiguration angelOneOptions,
            string jwtToken = ""
        )
        {
            httpRequestMessage.Headers.Add("X-PrivateKey", angelOneOptions.ApiKey);
            httpRequestMessage.Headers.Add("X-SourceID", "WEB");
            httpRequestMessage.Headers.Add("X-UserType", "USER");
            httpRequestMessage.Headers.Add("X-ClientLocalIP", Util.GetLocalIp());
            var publicIp = string.IsNullOrWhiteSpace(angelOneOptions.PublicIp)
                ? Environment.GetEnvironmentVariable("ANGEL_PUBLIC_IP") ?? string.Empty
                : angelOneOptions.PublicIp;
            httpRequestMessage.Headers.Add("X-ClientPublicIP", publicIp);
            httpRequestMessage.Headers.Add("X-MACAddress", Util.GetMacAddress());
            httpRequestMessage.Headers.Add("Accept", "application/json");

            if (jwtToken != string.Empty)
            {
                httpRequestMessage.Headers.Add("Authorization", $"Bearer {jwtToken}");
            }
        }

        public static void AddContent(
            this HttpRequestMessage httpRequestMessage,
            object requestBody
        )
        {
            httpRequestMessage.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );
        }
    }
}
