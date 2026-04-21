using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace InionVR.AI
{
    public class Configuration
    {
        public Auth Auth { get; }

        private readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver()
            {
                NamingStrategy = new CustomNamingStrategy()
            }
        };

        public Configuration(string apiKey, string organization = null)
        {
            Auth = new Auth()
            {
                ApiKey = apiKey,
                Organization = organization
            };
        }
    }

    public struct Auth
    {
        [JsonRequired]
        public string ApiKey { get; set; }
        public string Organization { get; set; }

        public Auth(string apiKey, string organization)
        {
            ApiKey = apiKey;
            Organization = organization;
        }
    }

    public class CustomNamingStrategy : NamingStrategy
    {
        protected override string ResolvePropertyName(string name)
        {
            var result = Regex.Replace(name, "([A-Z])", m => (m.Index > 0 ? "_" : "") + m.Value[0].ToString().ToLowerInvariant());
            return result;
        }
    }

    public struct FileData
    {
        public byte[] Data;
        public string Name;
    }
    
    public class CreateAudioRequestBase
    {
        public string File { get; set; }
        public FileData FileData { get; set; }
        public string Model { get; set; }
        public string Prompt { get; set; }
        public string ResponseFormat { get; set; } = AudioResponseFormat.Json;
        public float? Temperature { get; set; } = 0;
    }

    public class CreateAudioFromTextRequest : CreateAudioRequestBase
    {
        public string Input { get; set; }
        public string Voice { get; set; }
        public string Language { get; set; }
    }

    public class CreateAudioTranscriptionsRequest : CreateAudioRequestBase
    {
        public string Language { get; set; }
    }

    public class CreateAudioTranslationRequest : CreateAudioRequestBase { }

    public struct CreateAudioResponse : IResponse
    {
        public ApiError Error { get; set; }
        public string Warning { get; set; }
        public string Text { get; set; }
    }

    public interface IResponse
    {
        ApiError Error { get; set; }
        public string Warning { get; set; }
    }

    public class ApiError
    {
        public string Message;
        public string Type;
        public object Param;
        public object Code;
    }
    public static class AudioResponseFormat
    {
        public const string Json = "json";
        public const string Text = "text";
        public const string Srt = "srt";
        public const string VerboseJson = "verbose_json";
        public const string Vtt = "vtt";
    }

    public static class ContentType
    {
        public const string MultipartFormData = "multipart/form-data";
        public const string ApplicationJson = "application/json";
    }
}

[CreateAssetMenu(fileName = "OpenAIConfig", menuName = "VoiceBOT/Config")]
public class OpenAIConfiguration : ScriptableObject
{
    public string assistantID;
    public string openAiApiKey;
}
