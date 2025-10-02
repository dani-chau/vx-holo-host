// Vladislava Simakov
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Microsoft.CognitiveServices.Speech;
using Piper;

public class HelloWorld : MonoBehaviour
{
    public static HelloWorld Instance;

    // Hook up the three properties below with a Text, InputField and Button object in your UI.
    public Text outputText;
    public InputField inputField;
    public Button speakButton;
    public AudioSource audioSource;

    private const int SampleRate = 24000;
    private object threadLocker = new object();
    private bool waitingForSpeak;
    public bool audioSourceNeedStop;
    private string message;
    
    private SpeechConfig speechConfig;
    private SpeechSynthesizer synthesizer;

    [Header("Piper")]
    public PiperManager piper;
    [SerializeField, Tooltip("Check this to force Vivi to use local TTS (Piper) instead of Azure.")]
    private bool usePiper = false;
    Whisper1 whisperInstance;
    //bool azureAvailable = true;

    public void SynthesizeSpeech(string text)
    {
        lock (threadLocker)
        {
            waitingForSpeak = true;
        }

        string newMessage = null;
        var startTime = DateTime.Now;


        if (IsAzureAvailable())
        {
            // Starts speech synthesis, and returns once the synthesis is started.
            using (var result = synthesizer.StartSpeakingTextAsync(text).Result)
            {
                // Native playback is not supported on Unity yet (currently only supported on Windows/Linux Desktop).
                // Use the Unity API to play audio here as a short term solution.
                // Native playback support will be added in the future release.
                var audioDataStream = AudioDataStream.FromResult(result);
                var isFirstAudioChunk = true;

                var audioClip = AudioClip.Create(
                    "Speech",
                    SampleRate * 600, // Can speak 10mins audio as maximum
                    1,
                    SampleRate,
                    true,
                    (float[] audioChunk) =>
                    {
                        var chunkSize = audioChunk.Length;
                        var audioChunkBytes = new byte[chunkSize * 2];
                        var readBytes = audioDataStream.ReadData(audioChunkBytes);
                        if (isFirstAudioChunk && readBytes > 0)
                        {
                            var endTime = DateTime.Now;
                            var latency = endTime.Subtract(startTime).TotalMilliseconds;
                            //newMessage = $"Azure Speech synthesis succeeded!\nLatency: {latency} ms.";
                            //Debug.Log(newMessage);
                            isFirstAudioChunk = false;
                        }

                        for (int i = 0; i < chunkSize; ++i)
                        {
                            if (i < readBytes / 2)
                            {
                                audioChunk[i] = (short)(audioChunkBytes[i * 2 + 1] << 8 | audioChunkBytes[i * 2]) / 32768.0F;
                            }
                            else
                            {
                                audioChunk[i] = 0.0f;
                            }
                        }

                        if (readBytes == 0)
                        {
                            Thread.Sleep(200); // Leave some time for the audioSource to finish playback
                            audioSourceNeedStop = true;
                        }
                    });

                audioSource.clip = audioClip;
                audioSource.Play();
            }
        } else
        {
            UsePiperFallback(text);
        }
        

        lock (threadLocker)
        {
            if (newMessage != null)
                message = newMessage;

            waitingForSpeak = false;
        }
    }

    private async void UsePiperFallback(string text)
    {
        try
        {
            var audio = piper.TextToSpeech(text);
            var clip = await audio;

            audioSource.Stop();
            if (audioSource.clip)
                Destroy(audioSource.clip);

            audioSource.clip = clip;
            audioSource.Play();

            message = "Piper speech synthesis succeeded.";
            Debug.Log(message);
        }
        catch (Exception ex)
        {
            message = $"Piper speech synthesis failed: {ex.Message}";
            Debug.LogError(message);
        }
    }

    bool IsAzureAvailable()
    {
        if (usePiper)
        {
            return false;
        }
        else
        {
            // For now, we depend on if local transcription is used to use Piper
            //Whisper1 whisperInstance = FindObjectOfType<Whisper1>();
            return whisperInstance != null && whisperInstance.AzureAvailable;
        }
    }

    void Start()
    {
        Instance = this;
        whisperInstance = FindObjectOfType<Whisper1>();

        //Creates an instance of a speech config with specified subscription key and service region.
        speechConfig = SpeechConfig.FromSubscription(Key.subscriptionKey, Key.region);
        speechConfig.SpeechSynthesisVoiceName = "en-US-AriaNeural";
        speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm);

        //Creates a speech synthesizer.
        //Make sure to dispose the synthesizer after use!
        synthesizer = new SpeechSynthesizer(speechConfig, null);

        synthesizer.SynthesisCanceled += (s, e) =>
        {
            var cancellation = SpeechSynthesisCancellationDetails.FromResult(e.Result);
            message = $"Azure CANCELED:\nReason=[{cancellation.Reason}]\nErrorDetails=[{cancellation.ErrorDetails}]";
        };

        if (speakButton != null)
        {
            speakButton.interactable = !waitingForSpeak;
        }
    }
    void Update()
    {
        lock (threadLocker)
        {
            if (outputText != null)
            {
                outputText.text = message;
            }

            if (audioSourceNeedStop)
            {
                audioSource.Stop();
                audioSourceNeedStop = false;
            }
        }
    }

    void OnDestroy()
    {
        if (synthesizer != null)
        {
            synthesizer.Dispose();
        }
    }
}
