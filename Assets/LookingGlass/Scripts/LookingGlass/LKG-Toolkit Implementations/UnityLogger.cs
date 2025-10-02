using System;
using UnityEngine;
using ToolkitAPI;

namespace LookingGlass {
    public class UnityLogger : ToolkitAPI.ILogger {
        public void Log(string message) {
            Debug.Log(message);
        }

        public void LogException(Exception e) {
            Debug.LogError(e.ToString());
        }
    }
}
