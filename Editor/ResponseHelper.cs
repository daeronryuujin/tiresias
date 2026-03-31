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

        public static void SendPng(HttpListenerResponse res, byte[] pngData)
        {
            res.StatusCode = 200;
            res.ContentType = "image/png";
            res.ContentLength64 = pngData.Length;
            try
            {
                res.OutputStream.Write(pngData, 0, pngData.Length);
            }
            finally
            {
                res.OutputStream.Close();
            }
        }
    }
}
