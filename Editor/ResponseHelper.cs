using System.Net;
using System.Text;

namespace Tiresias
{
    public static class ResponseHelper
    {
        public static void Send(HttpListenerResponse res, int statusCode, string jsonBody)
        {
            res.StatusCode = statusCode;
            res.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            res.ContentLength64 = bytes.Length;
            try
            {
                res.OutputStream.Write(bytes, 0, bytes.Length);
            }
            finally
            {
                res.OutputStream.Close();
            }
        }
    }
}
