using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace InionVR.AI
{
    public delegate void VoiceBotEvent();
    public delegate void VoiceBotProcessingEvent(string processingOn);
    public delegate void VoiceBotCompletedEvent(AudioClip response);
    public class VoiceBot
    {
        #region Public variables ---------------------------------------------------------

        /// <summary>
        /// The number of microphone device to use. could be updated using the public functions
        /// </summary>
        private int MicNumber;

        /// <summary>
        /// On listening event will be called once the Microphone has been set up and start recording.
        /// </summary>
        public VoiceBotEvent onListening;

        /// <summary>
        /// On Thinking will be called when the Audio has successfully been converted to Text and sent to the AI for generating the answer.
        /// </summary>
        public VoiceBotProcessingEvent onThinking;

        /// <summary>
        /// On Speaking Will be called once the reply text has been received from AI and has been successfully converted to an audio clip.
        /// </summary>
        public VoiceBotProcessingEvent onSpeaking;

        /// <summary>
        /// On Completed Will be called once the received audio has been played and finished completely. It marks the ending of the first cycle and a new cycle could begin.
        /// </summary>
        public VoiceBotCompletedEvent onCompleted;

        #endregion


        #region Private variables -----------------------------------------------------
        private bool isThreadRunning, isClipRecording;
        private List<string> userDiary;
        private string fileName, threadID, assistantID;
        private AudioClip clip;
        private CancellationTokenSource cancellationTokenSource;
        private Configuration configuration;
        private const string BASE_PATH = "https://api.openai.com/v1";
        #endregion


        #region Constructors And Destructors ------------------------------------------------
        /// <summary>
        /// Use this constructor to initiate the values. Destroy this later when not needed or need to make a new thread.
        /// </summary>
        /// <param name="assistantID">ID of the Open AI Assistant to use</param>
        /// <param name="APIKey">The API Key for the Open AI account. Please keet this secured and dont ship it.</param>
        /// <param name="micNum">The index of microphone to use. Check VoiceBot.GetMicrophoneList() to see all and VoiceBot.SetMicrophone() to update it.</param>
        public VoiceBot(string assistantID, string APIKey, int micNum = 0)
        {
            //construct
            this.assistantID = assistantID;
            configuration = new Configuration(APIKey);
            cancellationTokenSource = new CancellationTokenSource();
            fileName = "output.wav";
            MicNumber = micNum;
            PlayerPrefs.SetString("threadID", "");
        }

        ~VoiceBot()
        {
            //dispose of all threads
            cancellationTokenSource.Cancel();
            Debug.Log("Voice bot data was disposed correctly.");
        }
        #endregion


        #region Public Functions ------------------------------------------------------
        /// <summary>
        /// This Sets which Microphone to use for recording voice. See GetMicrophoneList() for full list.
        /// </summary>
        /// <param name="index">Microphone Index</param>
        public void SetMicrophone(int index)
        {
            MicNumber = index;
        }

        /// <summary>
        /// This retrive all useable mics. See SetMicrophone() to set it.
        /// </summary>
        /// <returns>Array of Strings: Names of available Mics</returns>
        public string[] GetMicrophoneList()
        {
            return Microphone.devices;
        }

        /// <summary>
        /// I use this for internal debugging but you can use it too :)
        /// </summary>
        /// <param name="obj"></param>
        private void PrintClass(object obj)
        {
            Debug.Log(JsonConvert.SerializeObject(obj));
        }

        /// <summary>
        /// Calling this function will start the Bot cycle of Record-Think-Reply. Handle events before calling.
        /// </summary>
        /// <param name="recordingLength">The time in seconds for recording.</param>
        public void Record(int recordingLength)
        {
            Debug.Log("Checking for existing requests...");
            if (!isThreadRunning)
            {
                Debug.Log("Recording...");
                Record(cancellationTokenSource.Token, recordingLength);
            }
            else
                Debug.Log("A request is already in progress, discarding this one.");
        }

        /// <summary>
        /// Use this to stop the recording safely in between and proceed with whatever is recorded till now.
        /// </summary>
        public void StopRecording()
        {
            isClipRecording = false;
        }

        /// <summary>
        /// Call this in OnDestroy method to destroy the script as soon as unity runtime dies
        /// </summary>
        public void Destroy()
        {
            cancellationTokenSource.Cancel();
        }
        #endregion


        #region Main Loop --------------------------------------------------------------
        /// <summary>
        /// This is the process of recording from mic and fetching the mp3 file, than fetching the string from it.
        /// </summary>
        /// <param name="token">cancellation token</param>
        /// <param name="recordingLength">length of recording</param>
        private async void Record(CancellationToken token, int recordingLength)
        {
            isThreadRunning = true;
            try
            {
                //Start recording
                if (!cancellationTokenSource.IsCancellationRequested)
                    onListening.Invoke();
                Debug.Log("Recording on " + MicNumber + ":" + Microphone.devices[MicNumber]);
                clip = Microphone.Start(Microphone.devices[MicNumber], false, recordingLength, 44100);

                isClipRecording = true;
                float tempRecordingLength = recordingLength;

                while (isClipRecording && !token.IsCancellationRequested)
                {
                    await Task.Delay(100);
                    tempRecordingLength -= 0.1f;
                    if (tempRecordingLength < 0) isClipRecording = false;
                };

                Debug.Log("Recorded a " + (recordingLength - tempRecordingLength) + " second clip.");
                Microphone.End(null);

                //recording complete
                //save recording file

                //byte[] data = SaveWav.Save(fileName, clip);

                int micPosition = Microphone.GetPosition(Microphone.devices[MicNumber]);
                Microphone.End(null);

                clip = TrimClip(clip, micPosition);
                byte[] data = SaveWav.Save(fileName, clip);


                //Speech to text using wisper

                var req = new CreateAudioTranscriptionsRequest
                {
                    FileData = new FileData() { Data = data, Name = "audio.wav" },
                    Model = "gpt-4o-mini-transcribe", //"whisper-1"
                    Language = "en",
                    Prompt =
                        "The audio is spoken in English and contains voice commands about human anatomy, organs, and body systems. The speaker may use command words such as add, remove, delete, hide, or show, followed by an organ or system name. Transcribe all command words and anatomical terms accurately. Use full English anatomical terms, not single letters (for example, use eye instead of I). Common anatomy terms include heart, lungs, liver, stomach, intestines, reproductive system, skeletal system, muscular system, respiratory system, nervous system, eye, muscles."

                };
                //old method sending the data forward to gpt, removing this
                /*var res = await CreateAudioTranscription(req, token);
                if (!token.IsCancellationRequested)
                    onThinking.Invoke(res);
                else return;

                // send generated text to gpt

                Debug.Log("Sending \"" + res + "\".");
                if (res != "-1")
                    GenerateResponse(res, token);*/

                var res = await CreateAudioTranscription(req, token);

                if (token.IsCancellationRequested)
                    return;

                // THIS IS NOW THE FINAL OUTPUT
                onThinking.Invoke(res);

                // HARD STOP — do NOT continue pipeline
                isThreadRunning = false;
                return;

            }
            catch (OperationCanceledException)
            {
                Microphone.End(null);
                Debug.Log("Recording Cancelled.");
            }
        }

        private AudioClip TrimClip(AudioClip clip, int samples)
        {
            if (clip == null || samples <= 0)
                return clip;

            float[] data = new float[samples * clip.channels];
            clip.GetData(data, 0);

            AudioClip trimmedClip = AudioClip.Create(
                clip.name + "_trimmed",
                samples,
                clip.channels,
                clip.frequency,
                false
            );

            trimmedClip.SetData(data, 0);
            return trimmedClip;
        }


        /// <summary>
        /// this is to generate a text response from the user voice prompt.
        /// </summary>
        /// <param name="input">string prompt</param>
        /// <param name="token">cancellation token</param>
        private async void GenerateResponse(string input, CancellationToken token)
        {
            isThreadRunning = true;

            //check if thread ID exists, If not make one

            threadID = PlayerPrefs.GetString("threadID");

            if (threadID == null || threadID == "")
            {
                threadID = await CreateThread(token);
                PlayerPrefs.SetString("threadID", threadID);
                Debug.Log("Made a new thread ID: " + threadID);
            }
            else if (!await ThreadExists(threadID, token))
            {
                threadID = await CreateThread(token);
                PlayerPrefs.SetString("threadID", threadID);
                Debug.Log("Made a new thread ID because saved one dosen't exist: " + threadID);
            }
            else
                Debug.Log("Using old thread with id:" + threadID);



            if (token.IsCancellationRequested)
                return;
            string answer = await GetResponseFromGPT(threadID, input, token);

            if (token.IsCancellationRequested) return;

            //send querry to chat GPT
            await Speak(answer, token);
        }

        /// <summary>
        /// this is to convert the text response back to an mp3 file and later to a unity audio clip.
        /// </summary>
        /// <param name="text">name of the mp3</param>
        /// <param name="token">cancellation token</param>
        /// <returns>returns a blank task</returns>
        private async Task Speak(string text, CancellationToken token)
        {
            isThreadRunning = true;
            if (!cancellationTokenSource.IsCancellationRequested)
                onSpeaking.Invoke(text);

            //request for an Text to speech conversion
            var req = new CreateAudioFromTextRequest
            {
                Model = "tts-1",
                Input = text,
                Voice = "onyx",
                ResponseFormat = "mp3"
            };
            Debug.Log("asking for the file");
            var res = await CreateAudioFromText(req, token);

            if (token.IsCancellationRequested)
                return;

            string location = Path.Combine(Application.persistentDataPath, "fromGPT.mp3");

            //save all bytes as mp3 to be used later
            File.WriteAllBytes(location, res);

            string filePath = "file://" + location;

            //convert mp3 to unity audio clip
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(filePath, AudioType.MPEG))
            {
                var operation = www.SendWebRequest();

                while (!operation.isDone && !token.IsCancellationRequested)
                    await Task.Yield();

                if (token.IsCancellationRequested)
                {
                    www.Abort();
                    www.Dispose();
                    return;
                }

                if (www.result == UnityWebRequest.Result.ConnectionError)
                {
                    Debug.Log("Yahi ka hai");
                    Debug.Log(www.error);
                    return;
                }
                else
                {
                    //if all is okay provide result
                    Microphone.End(null);
                    if (!cancellationTokenSource.IsCancellationRequested)
                    {
                        onCompleted.Invoke(DownloadHandlerAudioClip.GetContent(www));
                    }
                }
            }
            isThreadRunning = false;
        }
        #endregion


        #region High-Level API Calls -------------------------------------------

        async Task<string> CreateAudioTranscription(CreateAudioTranscriptionsRequest request, CancellationToken token)
        {
            var path = $"{BASE_PATH}/audio/transcriptions";

            var form = new List<IMultipartFormSection>();
            if (string.IsNullOrEmpty(request.File))
            {
                form.AddData(request.FileData, "file", $"audio/{Path.GetExtension(request.File)}");
            }
            else
            {
                form.AddFile(request.File, "file", $"audio/{Path.GetExtension(request.File)}");
            }
            form.AddValue(request.Model, "model");
            form.AddValue(request.Prompt, "prompt");
            form.AddValue(request.ResponseFormat, "response_format");
            form.AddValue(request.Temperature, "temperature");
            form.AddValue(request.Language, "language");

            return await DispatchRequest(path, form, token);
        }

        async Task<bool> ThreadExists(string threadID, CancellationToken token)
        {
            var path = $"{BASE_PATH}/threads/" + threadID;
            var result = await CallOpenAiApi<ThreadDetails>(path, "GET", token);
            if (token.IsCancellationRequested) return false;
            return (result.id == threadID);
        }

        async Task<string> CreateThread(CancellationToken token)
        {
            var path = $"{BASE_PATH}/threads";
            var result = await CallOpenAiApi<ThreadDetails>(path, "POST", token);
            if (token.IsCancellationRequested) return "";
            return (result.id);
        }

        async Task<string> GetResponseFromGPT(string threadID, string message, CancellationToken token)
        {
            var path = $"{BASE_PATH}/threads/" + threadID + "/messages";
            MessageObject messageResponse = await CallOpenAiApi<MessageObject>(path, "POST", token, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new MessagePayload(message))));
            if (token.IsCancellationRequested) return null;
            if (messageResponse.id == null || messageResponse.id == "")
            {
                Debug.Log("Errored 0");
                Debug.Log(messageResponse.error.message);

                return null;
            }

            path = $"{BASE_PATH}/threads/" + threadID + "/runs";
            RunObject runResponse = await CallOpenAiApi<RunObject>(path, "POST", token, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new RunPayload(assistantID))));

            if (token.IsCancellationRequested) return null;

            if (runResponse.id == null || runResponse.id == "")
            {
                Debug.Log("Errored 1");
                Debug.Log(runResponse.error.message);
                return null;
            }

            path = $"{BASE_PATH}/threads/" + threadID + "/messages";

            bool waiting = true;
            ThreadData threadData;
            while (waiting && !token.IsCancellationRequested)
            {
                threadData = await CallOpenAiApi<ThreadData>(path, "GET", token);

                if (token.IsCancellationRequested) return null;

                if (threadData.first_id != null && threadData.first_id != "")
                {
                    messageResponse = await CallOpenAiApi<MessageObject>((path + "/" + threadData.first_id), "GET", token);

                    if (token.IsCancellationRequested) return null;

                    if (messageResponse.role == "assistant")
                        if (messageResponse.content.Length != 0)
                            waiting = false;
                }
            }

            return messageResponse.content[0].text.value;
        }

        async Task<byte[]> CreateAudioFromText(CreateAudioFromTextRequest request, CancellationToken token)
        {
            var path = "https://api.openai.com/v1/audio/speech";

            string jsonData = JsonUtility.ToJson(new body(request.Input));
            return await DispatchRequest(path, "POST", token, System.Text.Encoding.UTF8.GetBytes(jsonData));
        }

        #endregion


        #region Low-Level API Calls -------------------------------------------

        private async Task<byte[]> DispatchRequest(string path, string method, CancellationToken token, byte[] payload = null)
        {
            byte[] data;
            using (var request = UnityWebRequest.Put(path, payload))
            {
                request.method = method;
                request.SetHeaders(configuration, ContentType.ApplicationJson);

                var asyncOperation = request.SendWebRequest();

                while (!asyncOperation.isDone && !token.IsCancellationRequested)
                    await Task.Yield();

                if (token.IsCancellationRequested)
                {
                    request.Abort();
                    request.Dispose();
                    return new byte[0];
                }

                data = request.downloadHandler.data;
            }
            return data;
        }

        private async Task<string> DispatchRequest(string path, List<IMultipartFormSection> form, CancellationToken token)
        {
            string data;
            using (var request = new UnityWebRequest(path, "POST"))
            {
                request.SetHeaders(configuration);
                var boundary = UnityWebRequest.GenerateBoundary();
                var formSections = UnityWebRequest.SerializeFormSections(form, boundary);
                var contentType = $"{ContentType.MultipartFormData}; boundary={Encoding.UTF8.GetString(boundary)}";
                request.uploadHandler = new UploadHandlerRaw(formSections) { contentType = contentType };
                request.downloadHandler = new DownloadHandlerBuffer();
                var asyncOperation = request.SendWebRequest();

                while (!asyncOperation.isDone && !token.IsCancellationRequested)
                    await Task.Yield();

                if (token.IsCancellationRequested)
                {
                    request.Abort();
                    request.Dispose();
                    return "-1";
                }

                data = JsonUtility.FromJson<test>(request.downloadHandler.text).text;
                return data;
            }
        }

        private async Task<T> CallOpenAiApi<T>(string path, string method, CancellationToken token, byte[] payload = null)
        {
            T data;

            using (var request = new UnityWebRequest(path, method))
            {

                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + configuration.Auth.ApiKey);
                request.SetRequestHeader("OpenAI-Beta", "assistants=v2");
                request.uploadHandler = (UploadHandler)new UploadHandlerRaw(payload);
                request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();

                var asyncOperation = request.SendWebRequest();

                while (!asyncOperation.isDone && !token.IsCancellationRequested)
                    await Task.Yield();

                if (token.IsCancellationRequested)
                {
                    Debug.Log("Aborting");
                    request.Abort();
                    request.Dispose();
                    return JsonConvert.DeserializeObject<T>("", jsonSerializerSettings);
                }

                data = JsonConvert.DeserializeObject<T>(request.downloadHandler.text, jsonSerializerSettings);
            }
            return data;
        }

        #endregion


        #region Local Data Structures-----------------------------------------------------

        private readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver()
            {
                NamingStrategy = new CustomNamingStrategy()
            },
            Culture = CultureInfo.InvariantCulture
        };

        public class ThreadData
        {
            public List<MessageObject> data;
            public string first_id;
            public string last_id;
            public string has_more;
        }

        public class RunObject
        {
            public string id;
            public string status;
            public Error error;
        }

        public class MessageObject
        {
            public string id;
            public string role;
            public Content[] content;
            public Error error;
        }

        public class Content
        {
            public string type;
            public TextT text;
        }

        public class TextT
        {
            public string value;
        }

        public class MessagePayload
        {
            public string role = "user";
            public string content;

            public MessagePayload(string content)
            {
                this.content = content;
            }
        }

        public class RunPayload
        {
            public string assistant_id;

            public RunPayload(string assistant_id)
            {
                this.assistant_id = assistant_id;
            }
        }

        public class ThreadDetails
        {
            public string id;
        }

        public class Error
        {
            public string message;
            public string type;
            public string param;
            public string code;
        }

        public class test
        {
            public string text;
        }

        public class body
        {
            public string model = "tts-1";
            public string input = "The quick brown fox jumped over the lazy dog.";
            public string voice = "shimmer";
            public body(string i)
            {
                input = i;
            }
            public body(string m, string i, string v)
            {
                model = m;
                input = i;
                voice = v;
            }
        }
        #endregion#
    }
}
