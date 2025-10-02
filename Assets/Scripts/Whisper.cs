using UnityEngine;
using UnityEngine.UI;
using Microsoft.CognitiveServices.Speech;
using System.Collections;
using System.Threading.Tasks;
using System.IO;
using Whisper.Utils;
using Whisper;
using PimDeWitte.UnityMainThreadDispatcher;
using System;

public class SpeechRecognition : MonoBehaviour
{
    public Text outputText;
    public Button startRecoButton;
    public Image progressBar;

    private object threadLocker = new object();
    private bool waitingForReco;
    private string message;
    private float time;
    private readonly int duration = 15;

    private bool micPermissionGranted = false;
    private bool commandReadyToSend;
    private string curCommand;

    private SpeechRecognizer speechRecognizer;
    private SpeechConfig speechConfig;
    private bool isListening = false;
    private bool keywordDetected = false;
    private readonly string keyword = "Hi Vivi"; // The keyword to listen for
    private string modelFilePath;

    [Header("Local transcription")]
    [Tooltip("Check this to true to force Vivi to use local transcription. Useful for testing.")]
    public bool useLocalTranscription = false;
    public WhisperManager whisper;
    public MicrophoneRecord microphoneRecord;
    private WhisperStream _stream;
    // These include common words Whisper tiny model thinks the keyword sounds like.
    private readonly string[] keywordNames = { "Vivi", "VV", "baby", "BV" };
    
    [SerializeField, HideInInspector]
    private bool azureAvailable = true;
    // A public getter for use in ManagerTestScript and Whisper
    public bool AzureAvailable => azureAvailable;


    private void Start()
    {
        if (outputText == null)
        {
            UnityEngine.Debug.LogError("outputText property is null! Assign a UI Text element to it.");
        }
        else if (startRecoButton == null)
        {
            message = "startRecoButton property is null! Assign a UI Button to it.";
            UnityEngine.Debug.LogError(message);
        }
        else
        {
            micPermissionGranted = true;
            message = "Speak into your microphone.";
            startRecoButton.onClick.AddListener(StartListening);
        }

        try
        {
            if (useLocalTranscription)
            {
                Debug.Log("useLocalTranscription in Whisper game object is checked. Using local transcription.");
                azureAvailable = false;
            }
            else if (Key.subscriptionKey.Length == 0)
            {
                Debug.Log("subscriptionKey is missing from Key.cs. Using local transcription.");
                azureAvailable = false;
            }
            else if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                Debug.Log("No internet connection! Using local transcription.");
                azureAvailable = false;
            }
            else
            {
                // Use the original Azure model
                // Handle the model file path
                string modelFile = "669dc1cf-9942-41f9-baa5-273e674e71ce.table";
                string persistentFilePath = Path.Combine(Application.persistentDataPath, modelFile);

                if (!File.Exists(persistentFilePath))
                {
                    Debug.Log("Model file not in Persistent path");
                    string streamingAssetPath = Path.Combine(Application.streamingAssetsPath, modelFile);

                    // Load from StreamingAssets and copy to persistent data path
                    File.Copy(streamingAssetPath, persistentFilePath);
                    Debug.Log("Model file copied to Persistent path");
                }

                modelFilePath = persistentFilePath;

                // Initialize speech configuration
                speechConfig = SpeechConfig.FromSubscription(Key.subscriptionKey, Key.region);
            }

        } 
        catch (Exception ex)
        {
            Debug.LogError($"Azure init failed: {ex.Message}");
            azureAvailable = false;
        }
    }

    private async Task StartLocalTranscription()
    {
        _stream = await whisper.CreateStream(microphoneRecord);
        _stream.OnSegmentUpdated += OnSegmentUpdated;
        _stream.OnSegmentFinished += OnSegmentFinished;
        Debug.Log("Local transcription started!");
        _stream.StartStream();
        microphoneRecord.StartRecord();
    }

    private void Update()
    {
        lock (threadLocker)
        {
            if (startRecoButton != null)
            {
                startRecoButton.interactable = !waitingForReco && micPermissionGranted;
            }

            if (outputText != null)
            {
                outputText.text = message;
            }

            if (commandReadyToSend)
            {
                commandReadyToSend = false;
                CommandCompleted();
            }
        }
    }

    private async void StartListening()
    {
        if (isListening) return;

        isListening = true;
        Debug.Log("Speak into your microphone.");

        if (!azureAvailable)
        {
            await StartLocalTranscription();
        } 
        else
        {
            await ContinuousRecognitionWithKeywordSpottingAsync().ConfigureAwait(false);
        }
    }

    public async Task ContinuousRecognitionWithKeywordSpottingAsync()
    {
        var model = KeywordRecognitionModel.FromFile((string)modelFilePath);
        // Creates a speech recognizer using microphone as audio input.
        using (var recognizer = new SpeechRecognizer(speechConfig))
        {
            // Subscribes to events.
            recognizer.Recognizing += (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizingKeyword)
                {
                    Debug.Log($"RECOGNIZING KEYWORD: Text={e.Result.Text}");
                }
                else if (e.Result.Reason == ResultReason.RecognizingSpeech)
                {
                    Debug.Log($"RECOGNIZING: Text={e.Result.Text}");
                }
            };

            recognizer.Recognized += async (s, e) =>
            {
                var result = e.Result;
                var transcription = result.Text.Trim();
                Debug.Log(transcription);

                if (result.Reason == ResultReason.RecognizedKeyword)
                {
                    Debug.Log($"RECOGNIZED KEYWORD: Text={e.Result.Text}");
                    Debug.Log("Keyword recognized, waiting for command...");
                    message = "Keyword recognized...";
                    keywordDetected = true; // Set the flag when keyword is recognized
                }
                else if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    // Speech result is finalised and is ready to send as a command
                    Debug.Log("Recognized: " + transcription);
                    if (keywordDetected && isListening)
                    {
                        // Remove the keyword from the transcription
                        string command = transcription.Replace(keyword, "").Trim();
                        curCommand = command;
                        commandReadyToSend = true;
                        keywordDetected = false; // Reset the flag after capturing the command
                    }
                }
                else if (result.Reason == ResultReason.NoMatch)
                {
                    message = "No speech could be recognized.";
                    Debug.Log("No speech could be recognized.");
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = CancellationDetails.FromResult(result);
                    Debug.LogError("Error details: " + cancellation.ErrorDetails);
                    Debug.LogError("Did you set the speech resource key and region values?");
                    Debug.Log("Incorrect Azure key or region values. Using local transcription instead.");
                    await recognizer.StopKeywordRecognitionAsync().ConfigureAwait(false);
                    UnityMainThreadDispatcher.Instance().Enqueue(() => StartLocalTranscription());
                    message = "Speak into your microphone.";
                }
            };

            recognizer.Canceled += async (s, e) =>
            {
                Debug.Log($"CANCELED: Reason={e.Reason}");

                if (e.Reason == CancellationReason.Error)
                {
                    Debug.Log($"CANCELED: ErrorCode={e.ErrorCode}");
                    Debug.Log($"CANCELED: ErrorDetails={e.ErrorDetails}");
                    Debug.Log($"CANCELED: Did you update the subscription info?");
                }

                Debug.Log("Azure transcription cancelled. Using local transcription instead.");
                azureAvailable = false;
                UnityMainThreadDispatcher.Instance().Enqueue(() => StartLocalTranscription());
                message = "Speak into your microphone.";
            };

            recognizer.SessionStarted += (s, e) =>
            {
                Debug.Log("Session started event.");
            };

            recognizer.SessionStopped += (s, e) =>
            {
                Debug.Log("Session stopped event.");
                Debug.Log("Stop recognition.");
                if (azureAvailable)
                {
                    message = "Received: " + curCommand; // Update the message with the command
                }
            };

            // Starts continuous recognition using the keyword model.
            await recognizer.StartKeywordRecognitionAsync(model).ConfigureAwait(false);

            // Continue listening indefinitely
            while (isListening && azureAvailable)
            {
                await Task.Delay(100); // Small delay to prevent tight loop
            }

            await recognizer.StopKeywordRecognitionAsync().ConfigureAwait(false);
        }
    }

    private void CommandCompleted()
    {
        ManagerTestScript.instance.onSendCommand(curCommand);
    }

    private void OnDestroy()
    {
        speechRecognizer?.Dispose();
        isListening = false; // Ensure listening stops if the object is destroyed
    }

    private void OnSegmentUpdated(WhisperResult segment)
    {
        print($"Segment updated: {segment.Result}");
        if (DetectKeywordSegment(segment))
        {
            // check for keyword second so for the next command is processed without the "hey vivi" command
            keywordDetected = true;
            message = "Keyword recognized...";
        }
    }

    // This is called after the user stops talking and a new 'segment' of speech is complete
    private void OnSegmentFinished(WhisperResult segment)
    {
        print($"Segment finished and command is: {segment.Result}");
        if (keywordDetected)
        {
            string command = RemoveKeyword(segment.Result);
            Debug.Log($"Local transcription command sent: {command}");
            message = $"Received: {command}";
            curCommand = command; 
            commandReadyToSend = true;
            keywordDetected = false; // Reset the flag after capturing the command
        }
        
    }

    // Detect if what the user says contains the keyword
    // Note that for the local transcription, we check for keywords manually in each segment
    // So the user should pause and wait for the "Keyword recognized..." message to appear
    private bool DetectKeywordSegment(WhisperResult segment)
    {
        foreach (string keywordPhrase in keywordNames)
        {
            if (segment.Result.ToLower().Contains(keywordPhrase.ToLower()))
            {
                return true;
            }
        }
        return false;
    }

    /**
     * A hopefully smarter version of removing the keyword "Hi Vivi" used in parsing local transcription results
     * Designed to handle some weird outputs by the model, such as:
     *  - User needing to say Hi Vivi multiple times, so her name is included in the command multiple times too
     *  - The model sometimes adding a comma like "Hi, Vivi"
     */
    private string RemoveKeyword(string input)
    {
        // local transcription can add random whitespace sometimes
        input = input.TrimStart().TrimEnd();

        // in the case local transcription 
        foreach (string keywordName in keywordNames)
        {
            input = input.Replace(",", "").Replace(keywordName, "Vivi");
        }

        int KEYWORD_LENGTH = "Hi, Vivi".Length;
        // add 1 to account for possible full stop at the end.
        if (input.Length <= KEYWORD_LENGTH + 1)
        {
            return input;
        }

        string name = "vivi";
        int inputStartIndex = 0;
        string lowered = input.ToLower();
        // check if Vivi is still contained near the start of the string
        // to avoid edge case of having Vivi at end of comand
        // and, in case the command is just Hi Vivi multiple times, return just the one Hi Vivi at the end.
        int indexOf = lowered.IndexOf(name);
        while (indexOf > -1 && indexOf < "hi, v".Length && lowered.Length > KEYWORD_LENGTH + 1)
        {
            // add a one to account for possible comma.
            int keyphraseEndIndex = indexOf + name.Length + 1;
            inputStartIndex += keyphraseEndIndex;
            // Take off the keyword phrase
            lowered = lowered.Substring(keyphraseEndIndex);
            indexOf = lowered.IndexOf(name);
        }
        return input.Substring(inputStartIndex).TrimStart();
    }
}
