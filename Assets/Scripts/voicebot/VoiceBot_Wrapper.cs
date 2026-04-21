using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using InionVR.AI;

[RequireComponent(typeof(AudioSource))]
public class VoiceBot_Wrapper : MonoBehaviour
{
    [SerializeField] string a, k;
    [SerializeField] int micNumber;
    VoiceBot bot;

    [Header("Input")]
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.LTouch;
    private readonly OVRInput.Button holdToTalkButton = OVRInput.Button.One;   // A (left)
    bool talkingButtonHeld = false;
    [SerializeField] GameObject listening, loading;

   // public AfterVoiceButtonsManager afterVoiceButtonsManager;
    void Start()
    {
        // Turn off auto play
        GetComponent<AudioSource>().playOnAwake = false;

        // Initiate the VoiceBot
        bot = new VoiceBot(a, k, micNumber);

        // (Optional) Display all the Mics available
        foreach(var mic in bot.GetMicrophoneList()) 
            Debug.Log(mic);

        // Subscribe to the Listening Event
        bot.onListening += () =>
        {
            //ignore
        };

        // Subscribe to the Thinking Event
        bot.onThinking += (recordedPrompt) =>
        {
            loading.SetActive(false);

            Debug.Log("STT RESULT: " + recordedPrompt);

            //Prompt Result here, call spellmanager
        };

        // Subscribe to the Speaking Event
        bot.onSpeaking += (responce) =>
        {
            //ignore
        };

        // Subscribe to the Completion Event
        bot.onCompleted += (responceClip) =>
        {
            ///ignore
        };
    }

    private void Update()
    {

       // if (ControllerOrHandsUpdater.Instance.UpdateInputSource(controller == OVRInput.Controller.LTouch ? true : false)) return;

        // --- Talking (A) ---
        if (OVRInput.GetDown(holdToTalkButton, controller))
        {
            if (!talkingButtonHeld)
            {
                talkingButtonHeld = true;
                listening.SetActive(true);
                bot.Record(15);
            }
        }
        if (OVRInput.GetUp(holdToTalkButton, controller))
        {
            if (talkingButtonHeld)
            {
                talkingButtonHeld = false;
                listening.SetActive(false);
                loading.SetActive(true);
                bot.StopRecording();
            }
        }
    }

    public void OnDestroy()
    {
        bot.Destroy();
    }

    [ContextMenu("StopRecording")]
    public void StopRecording()
    {
        bot.StopRecording();
    }

    // Initiate the Cycle
    [ContextMenu("Record")]
    public void session()
    {
        bot.Record(14);
    }
}
