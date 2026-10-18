using System.Net;

namespace CodeName399.Kite.API.Helpers
{
    public class AngelApiException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        public string Response { get; }

        public AngelApiException(HttpStatusCode statusCode, string response)
            : base($"Angel API request failed ({statusCode})")
        {
            StatusCode = statusCode;
            Response = response;
        }
    }
}
