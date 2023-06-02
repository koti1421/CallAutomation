using Azure;
using Azure.Communication.CallAutomation;
using System.Net;
using System.Text;

namespace CallAutomationTest
{
    public static class CallAutomation
    {

        public static HttpWebResponse transferCall(this CallAutomationClient client, string callconnectionId)
        {
            var URL = "https://8708-2601-600-8780-dbc0-8c5a-8d9e-3c21-1c49.ngrok.io/calling/callConnections/" + callconnectionId + ":transferToParticipant?api-version=2022-04-07-preview";

            var request = (HttpWebRequest)WebRequest.Create(URL);

            var postData = "thing1=" + Uri.EscapeDataString("hello");
            postData += "&thing2=" + Uri.EscapeDataString("world");
            var data = Encoding.ASCII.GetBytes(postData);

            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = data.Length;

            using (var stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }

            var response = (HttpWebResponse)request.GetResponse();

            return response;
        }

       

    }
}
