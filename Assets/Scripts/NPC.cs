using Microsoft.CognitiveServices.Speech;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
public class NPC : MonoBehaviour
{
    private SpeechConfig speechConfig;
    private SpeechSynthesizer synthesizer;
    public Animator animator;
    public Text output_text;
    public Vector3 avator_posi;
    public string responseFilePath;
    private float _timeUntilBored;
    private int _numberOfBoredAnimations;
    private bool _isBored;
    private float _idleTime;
    private int _boredAnimation;
    private EntityResponse _responses;
    private LocationDatas _location;
    private HashSet<string> locationEntities;
    private HashSet<string> responseEntities;

    public enum Direction
    {
        FRONT = 0,
        RIGHT = 90,
        BACK = 180,
        LEFT = 270
    }

    private void Start()
    {
        locationEntities = new HashSet<string>();
        responseEntities = new HashSet<string>();

        // replace this with your key 
        string subscriptionKey = Key.subscriptionKey;
        string region = Key.region;

        speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
        speechConfig.SpeechSynthesisVoiceName = "en-US-AriaNeural";
        speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm);

        // Create the Speech Synthesizer
        synthesizer = new SpeechSynthesizer(speechConfig, null);
        animator = GetComponent<Animator>();

        // Load location data
        TextAsset locationJson = Resources.Load<TextAsset>("LocationData");
        if (locationJson != null)
        {
            _location = JsonUtility.FromJson<LocationDatas>(locationJson.text);
            foreach (var location in _location.location)
            {
                locationEntities.Add(location.CategoryKey.ToLower());
            }

            LocationData viviLoc = GetLocationFromEntity("Vivi", _location);
            if (viviLoc != null && viviLoc.locations != null)
            {
                avator_posi = viviLoc.locations.getPosition();
                Debug.Log("✅ Vivi position loaded from LocationData.json: " + avator_posi);
            }
            else
            {
                Debug.LogWarning("⚠️ Could not find Vivi's position, using origin (0,0,0)");
                avator_posi = Vector3.zero;
            }
        }
        else
        {
            Debug.LogError("❌ LocationData.json not found in Resources folder.");
            avator_posi = Vector3.zero;
        }

        _boredAnimation = UnityEngine.Random.Range(1, 3 + 1);
        _boredAnimation = _boredAnimation * 2 - 1;
        //animator.SetFloat("BoredIdle", 0);
        animator.SetFloat("BoredIdle", 1.46f);

        // Load location data
        locationJson = Resources.Load<TextAsset>("LocationData");
        if (locationJson != null)
        {
            _location = JsonUtility.FromJson<LocationDatas>(locationJson.text);
            foreach (var location in _location.location)
            {
                locationEntities.Add(location.CategoryKey.ToLower());
            }
        }
        else
        {
            Debug.LogError("LocationData.json not found in Resources.");
        }

        // Load response data
        TextAsset responseJson = Resources.Load<TextAsset>("ResponseDataFile");
        if (responseJson != null)
        {
            _responses = JsonUtility.FromJson<EntityResponse>(responseJson.text);
            foreach (var response in _responses.responses)
            {
                responseEntities.Add(response.CategoryKey.ToLower());
            }
        }
        else
        {
            Debug.LogError("ResponseDataFile.json not found in Resources.");
        }

        Debug.Log("Loaded location entities: " + string.Join(", ", locationEntities));
        Debug.Log("Loaded response entities: " + string.Join(", ", responseEntities));
    }

    public void Destroy()
    {
        Destroy(gameObject);
    }
    /*
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo)
        {
            ResetIdle();
        }

        // OnStateUpdate is called on each Update frame between OnStateEnter and OnStateExit callbacks
        override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (_isBored == false)
            {
                _idleTime += Time.deltaTime;

                if (_idleTime > _timeUntilBored && stateInfo.normalizedTime % 1 < 0.02f)
                {
                    _isBored = true;
                    _boredAnimation = UnityEngine.Random.Range(1, _numberOfBoredAnimations + 1);
                    _boredAnimation = _boredAnimation * 2 - 1;
                    animator.SetFloat("BoredIdle", _boredAnimation - 1);
                }
            }
            animator.SetFloat("BoredIdle", _boredAnimation, 0.2f, Time.deltaTime);
        }

        private void ResetIdle()
        {
            if (_isBored)
            {
                _boredAnimation--;
            }

            _isBored = false;
            _idleTime = 0;
        }

    */

    public LocationData GetLocationFromEntity(string category, LocationDatas locationWrapper)
    {
        // Search the responses list for the given category and get the releavent properties.
        LocationData locObj = new List<LocationData>(locationWrapper.location).FirstOrDefault(resp => resp.CategoryKey.ToLower() == category.ToLower());
        return locObj;
    }

    public Response GetResponseFromEntity(string category, EntityResponse responsesWrapper)
    {
        // Search the responses list for the given category and get the releavent properties.
        Response respObj = new List<Response>(responsesWrapper.responses).FirstOrDefault(resp => resp.CategoryKey.ToLower() == category.ToLower());
        return respObj;
    }

    // Read the user entered result and find the approriate response
    public void ReadResult(ConversationResult res)
    {
        string topIntent = res.result.prediction.topIntent;
        string response = "Please say again.";

        if (res != null && topIntent == "Location")
        {
            // Location intent handling
            foreach (var entity in res.result.prediction.entities)
            {
                LocationData locObj = GetLocationFromEntity(entity.category, _location);

                if (locObj != null)
                {
                    // Get the direction from the NPC to the location
                    Direction dir_to_point = getBestDirection(locObj.locations.getPosition());
                    string directionText = dir_to_point.ToString();

                    // Construct response with location description and direction
                    response = $"{locObj.description}{directionText}.";

                    // Log and output the final response
                    Debug.Log($"Location response: {response}");
                    StartCoroutine(UpdateOutputText(response));
                    HelloWorld.Instance.SynthesizeSpeech(response);

                    // Trigger the animation to point in the calculated direction
                    getEnumDirection(dir_to_point);
                }
                else
                {
                    Debug.LogWarning("Location not found for entity: " + entity.category);
                }
            }
        }
        else if (res != null && topIntent == "TellMe")
        {
            // Sometimes the category list is empty, so make sure Vivi explains that she is confused instead of not saying anything.
            if (res.result.prediction.entities.Length == 0)
            {
                response = "Sorry, I'm not sure about that.";
                //response = "The official hours are Monday to Friday, nine to five. After-hours and weekend access can be arranged.";
                StartCoroutine(UpdateOutputText(response));
                HelloWorld.Instance.SynthesizeSpeech(response);
                animator.SetTrigger("Offer");
                animator.SetFloat("BoredIdle", 3);
            }

            // General intent handling
            foreach (var entity in res.result.prediction.entities)
            {
                Response respObj = GetResponseFromEntity(entity.category, _responses);
                if (respObj != null)
                {
                    response = respObj.TextResponse;
                    Debug.Log("General response: " + response);

                    StartCoroutine(UpdateOutputText(response));
                    HelloWorld.Instance.SynthesizeSpeech(response);

                    // Trigger specific animation for general response
                    //animator.SetTrigger(respObj.AnimationTrigger + getRandomTrigger(1));
                    animator.SetTrigger(respObj.AnimationTrigger);
                }
                else
                {
                    Debug.LogWarning("Response not found for entity: " + entity.category);
                }
            }
        }
        else
        {
            StartCoroutine(UpdateOutputText(response));
            HelloWorld.Instance.SynthesizeSpeech(response);
            Debug.LogError("Intent not recognized or response data is missing.");
        }
    }

    //added these
    public void ReadAIResult(string airesponse, ConversationResult res)
    {
        Debug.Log("read result has been called");
        animator.SetBool("IsTalking", false);

        if (res.result.prediction.topIntent == "TellMe")
        {
            animator.SetTrigger("IsTalking0");
            HelloWorld.Instance.SynthesizeSpeech(airesponse);

        }
        else if (res.result.prediction.topIntent == "Location")
        {
            animator.SetTrigger("IsTalking1");
            HelloWorld.Instance.SynthesizeSpeech(airesponse);

        }
        else
        {
            Debug.Log(airesponse);
            animator.SetTrigger("IsTalking0");
            StartCoroutine(UpdateOutputText(airesponse));
            HelloWorld.Instance.SynthesizeSpeech(airesponse);
        }
    }

    public void HandleUserInput(string userMessage)
    {
        Debug.Log("Received input: " + userMessage);

        // Define multiple keywords to identify location requests
        string[] locationKeywords = { "location", "where" };

        // Check if any of the location keywords are present in the message
        bool isLocationRequest = locationKeywords.Any(keyword => userMessage.ToLower().Contains(keyword));

        // Extract the potential entity name from the message
        string entityCategory = ExtractEntityFromMessage(userMessage);

        // Handle location request using LocationData.json
        if (isLocationRequest && locationEntities.Contains(entityCategory))
        {
            LocationData locObj = GetLocationFromEntity(entityCategory, _location);

            if (locObj != null)
            {
                // Calculate direction
                Direction dir_to_point = getBestDirection(locObj.locations.getPosition());
                string directionText = dir_to_point.ToString();

                // Construct response message with location description and direction
                string locationMessage = locObj.description + directionText;
                Debug.Log("Location response: " + locationMessage);

                // Display and synthesize the response
                StartCoroutine(UpdateOutputText(locationMessage));
                HelloWorld.Instance.SynthesizeSpeech(locationMessage);

                // Trigger pointing animation based on direction
                getEnumDirection(dir_to_point);
                Debug.Log("Triggered directional animation: " + dir_to_point);
            }
            else
            {
                Debug.LogWarning("Location not found for entity: " + entityCategory);
            }
        }
        else if (!isLocationRequest && responseEntities.Contains(entityCategory))
        {
            // For general inquiries, use ResponseDataFile.json
            Response respObj = GetResponseFromEntity(entityCategory, _responses);

            if (respObj != null)
            {
                Debug.Log("Found general response: " + respObj.TextResponse);
                StartCoroutine(UpdateOutputText(respObj.TextResponse));
                HelloWorld.Instance.SynthesizeSpeech(respObj.TextResponse);

                // Trigger the response-specific animation
                string trigger = respObj.AnimationTrigger + getRandomTrigger(1);
                Debug.Log("Triggering animation: " + trigger);

                if (animator != null)
                {
                    animator.SetTrigger(trigger);
                }
                else
                {
                    Debug.LogError("Animator component is missing or not assigned.");
                }
            }
            else
            {
                Debug.LogWarning("No relevant information found for entity: " + entityCategory);
            }
        }
        else
        {
            Debug.LogWarning("Entity not found in either data source for: " + entityCategory);
        }
    }


    // Helper method to extract entity name from message using known entities
    private string ExtractEntityFromMessage(string message)
    {
        string lowerMessage = message.ToLower();

        // Check location entities first
        foreach (string entity in locationEntities)
        {
            if (lowerMessage.Contains(entity))
            {
                Debug.Log($"Matched location entity: {entity}");
                return entity;
            }
        }

        // Check response entities
        foreach (string entity in responseEntities)
        {
            if (lowerMessage.Contains(entity))
            {
                Debug.Log($"Matched response entity: {entity}");
                return entity;
            }
        }

        Debug.LogWarning("No matching entity found.");
        return "";  // Return empty if no entity is matched
    }

    private IEnumerator UpdateOutputText(string message)
    {
        output_text.text = message;
        yield return null;
    }

    //choose a random talking animation for naturalistic motion
    public string getRandomTrigger(int option)
    {
        string idx;
        int[] myTalkingAnims = { 0, 1 };
        int[] myIdleAnims = { 1, 2, 3 };
        int[] array;
        array = option > 0 ? myTalkingAnims : myIdleAnims;
        int length = array.Length;
        System.Random rand = new System.Random();
        int index = rand.Next(length);
        int chosen_anim = array[index];
        idx = chosen_anim.ToString();
        return idx;

    }

    void getDirection(string directionText)
    {
        switch (directionText)
        {
            case "forward":
                animator.SetTrigger("Forward");
                break;
            case "behind":
                animator.SetTrigger("Behind");
                break;
            case "left":
                animator.SetTrigger("LeftTrigger");
                break;
            case "right":
                animator.SetTrigger("RightTrigger");
                break;
            case "somewhere":
                animator.SetTrigger("Somewhere");
                break;
        }

    }

    // point to direction relative to users
    void getEnumDirection(Direction to_point)
    {
        switch (to_point)
        {
            case Direction.FRONT:
                animator.SetTrigger("Forward");
                break;
            case Direction.BACK:
                animator.SetTrigger("Behind");
                break;
            case Direction.LEFT:
                animator.SetTrigger("LeftTrigger");
                break;
            case Direction.RIGHT:
                animator.SetTrigger("RightTrigger");
                break;
        }
    }

    IEnumerator TriggerAnimations()
    {
        animator.SetTrigger("WaveTrigger");
        yield return new WaitForSeconds(9);
        animator.SetTrigger("LeftTrigger");
        yield return new WaitForSeconds(11);
        animator.SetTrigger("Behind");
        yield return new WaitForSeconds(7f);
        animator.SetTrigger("RightTrigger");
        yield return new WaitForSeconds(6);
        animator.SetTrigger("Forward");
    }

    Direction getBestDirection(Vector3 target_position)
    {
        Vector3 toTarget = target_position - avator_posi;
        Vector3 viviForward = Vector3.forward;

        Vector3 relativeDir = Quaternion.Inverse(Quaternion.LookRotation(viviForward)) * toTarget;
        float angle = Mathf.Atan2(relativeDir.x, relativeDir.z) * Mathf.Rad2Deg;
        angle = NormalizeAngle360(angle);

        if (angle >= 315 || angle < 45)
            return Direction.RIGHT;
        else if (angle >= 45 && angle < 135)
            return Direction.BACK;
        else if (angle >= 135 && angle < 225)
            return Direction.LEFT;
        else
            return Direction.FRONT;
    }




    /*     Direction getBestDirection(Vector3 target_position)
        {
            float degree_to_point = getDegree(target_position);

            Direction[] directions = (Direction[])Enum.GetValues(typeof(Direction));

            Direction best_direction = Direction.FRONT;
            float lowest_dif = 360;
            foreach (Direction dir in directions)
            {
                // float angle_diff = Mathf.Abs((degree_to_point % 360) - (float)dir);

                float angle_diff = Mathf.Min(Mathf.Abs((degree_to_point % 360) - (float)dir), 360 - Mathf.Abs((degree_to_point % 360) - (float)dir));

                if (angle_diff < lowest_dif)
                {
                    lowest_dif = angle_diff;
                    best_direction = dir;
                }
            }

            Debug.Log("best_direction: " + best_direction + ", lowest_dif: " + lowest_dif);
            return best_direction;

        } */

    private float getDegree(Vector3 target_position)
    {
        Debug.Log("----[getDegree START]----");

        Debug.Log("Vivi Position: " + avator_posi);
        Debug.Log("Target Position: " + target_position);

        Vector3 toTarget = target_position - avator_posi;
        toTarget.y = 0;

        Debug.Log("World toTarget Vector: " + toTarget);

        // Assume Vivi faces forward (Z+) in VXLab space
        Vector3 viviForward = Vector3.forward;

        Quaternion viviRotation = Quaternion.LookRotation(viviForward);
        Vector3 relativeDir = Quaternion.Inverse(viviRotation) * toTarget;

        Debug.Log("Local relativeDir (target in Vivi's POV): " + relativeDir);

        float angle = Mathf.Atan2(relativeDir.x, relativeDir.z) * Mathf.Rad2Deg;
        float normalized = NormalizeAngle360(angle);

        Debug.Log("Angle: " + angle + " => Normalized: " + normalized);
        Debug.Log("--------------------------");

        return normalized;
    }


    /*     private float getDegree(Vector3 target_position)
        {
            Vector3 targetDir = target_position - avator_posi;
            targetDir.y = 0; // Optional: Keep rotation in the horizontal plane only

            // Calculate the rotation needed to look at the target direction
            Quaternion targetRotation = Quaternion.LookRotation(targetDir);

            Vector3 eulerRotation = targetRotation.eulerAngles;
            float yRotation = eulerRotation.y;

            float yRotationNormalized = NormalizeAngle360(yRotation);

            Debug.Log("yRotationNormalized: " + yRotationNormalized);

            return yRotationNormalized;
        } */

    //Transform value into degress
    public static double DegreesToRadians(double angle)
    {
        return angle * Math.PI / 180.0d;
    }

    private float NormalizeAngle360(float angle)
    {
        return (angle % 360 + 360) % 360;
    }
}
