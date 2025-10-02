using System;
using UnityEngine;
using UnityEngine.UI;

public class TextInputHandler : MonoBehaviour
{
    public static TextInputHandler Instance;

    // Hook up the three properties below with a Text, InputField, and Button object in your UI.
    public Text outputText;
    public InputField inputField;
    public Button submitButton;
    public NPC npc; // Reference to the NPC script to handle responses

    private object threadLocker = new object();
    private bool waitingForInput;
    private string message;
    private string userMessage;

    void Start()
    {
        Instance = this;

        // Initial message prompting user to input something
        message = "Type something and click the button!";
        if (outputText != null)
        {
            outputText.text = message;
        }

        // Set up listener for the submit button to process input
        if (submitButton != null)
        {
            submitButton.onClick.AddListener(ProcessInput);
        }
    }

    // Function to handle text input from the user
    void ProcessInput()
    {
        lock (threadLocker)
        {
            waitingForInput = true;
        }

        if (inputField != null)
        {
            // Get the input text
            userMessage = inputField.text;

            if (!string.IsNullOrEmpty(userMessage))
            {
                // Pass the user's message to the NPC for processing
                if (npc != null)
                {
                    npc.ReadAIResult(userMessage, null); // Pass user input to NPC for handling
                }
                message = $"You said: {userMessage}";
            }
            else
            {
                message = "Please type something!";
            }

            // Output the processed message
            lock (threadLocker)
            {
                if (outputText != null)
                {
                    outputText.text = message;
                }
                waitingForInput = false;
            }
        }
    }

    void Update()
    {
        // Update the output text if no input is being processed
        lock (threadLocker)
        {
            if (!waitingForInput && outputText != null)
            {
                outputText.text = message;
            }
        }
    }
}
