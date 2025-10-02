using System;
using UnityEngine;
using UnityEngine.Networking;
using ToolkitAPI;

namespace LookingGlass {
    public class UnityHttpSender : IHttpSender {
        private int timeoutSeconds = 0;

        public int TimeoutSeconds {
            get { return timeoutSeconds; }
            set { timeoutSeconds = value; }
        }
        public Action<Exception> ExceptionHandler { get; set; }

        private UnityWebRequest CreateRequestInner(HttpSenderMethod method, string url, string content = null) {
            switch (method) {
                case HttpSenderMethod.Get:      return UnityWebRequest.Get(url);
                case HttpSenderMethod.Post:     return UnityWebRequest.PostWwwForm(url, content);
                case HttpSenderMethod.Put:      return UnityWebRequest.Put(url, content);
                default:
                    throw new NotSupportedException("Unsupported HTTP method: " + method);
            }
        }

        private UnityWebRequest CreateRequest(HttpSenderMethod method, string url, string content = null) {
            UnityWebRequest request = CreateRequestInner(method, url, content);
            request.timeout = timeoutSeconds;
            return request;
        }

        public string Send(HttpSenderMethod method, string url, string content) {
            UnityWebRequest request = CreateRequest(method, url, content);
            request.SendWebRequest();
            while (!request.isDone) { }

            string result = request.downloadHandler.text;
            request.FullyDispose();
            return result;
        }

        public void SendAsync(HttpSenderMethod method, string url, string content, Action<string> onCompletion) {
            UnityWebRequest request = CreateRequest(method, url, content);

            request.SendWebRequest().completed += operation => {
                onCompletion(request.downloadHandler.text);
                request.FullyDispose();
            };
        }
    }
}
