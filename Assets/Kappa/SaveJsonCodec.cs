using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace Kappa
{
    internal static class SaveJsonCodec
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new SaveContractResolver(),
            MissingMemberHandling = MissingMemberHandling.Error,
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            CheckAdditionalContent = true,
            MaxDepth = 64,
            Culture = CultureInfo.InvariantCulture
        };

        public static byte[] Encode<T>(T data) where T : class
        {
            ValidateType<T>();
            if (data == null) throw new ArgumentNullException(nameof(data));
            try
            {
                using var text = new StringWriter(CultureInfo.InvariantCulture);
                // 他機能が設定するJsonConvert.DefaultSettingsを保存形式へ持ち込まない。
                JsonSerializer.Create(Settings).Serialize(text, data);
                return Utf8.GetBytes(text.ToString());
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("Save data could not be serialized. Use data values, not Unity object references.", nameof(data), exception);
            }
        }

        public static T Decode<T>(byte[] payload) where T : class
        {
            ValidateType<T>();
            try
            {
                using var text = new StringReader(Utf8.GetString(payload));
                using var reader = new JsonTextReader(text);
                var data = JsonSerializer.Create(Settings).Deserialize<T>(reader);
                return data ?? throw new InvalidDataException("Save payload is empty.");
            }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
            {
                throw new InvalidDataException("Save payload could not be decoded.", exception);
            }
        }

        internal static void ValidateType<T>()
        {
            var type = typeof(T);
            if (!type.IsSerializable || type == typeof(string) || typeof(IEnumerable).IsAssignableFrom(type) ||
                typeof(UnityEngine.Object).IsAssignableFrom(type))
                throw new ArgumentException("Use a serializable data class for JSON saves, or use the byte API with another serializer.");
        }

        private sealed class SaveContractResolver : DefaultContractResolver
        {
            protected override JsonContract CreateContract(Type objectType)
            {
                if (typeof(UnityEngine.Object).IsAssignableFrom(objectType))
                    throw new JsonSerializationException("Persist stable identifiers instead of Unity object references.");
                return base.CreateContract(objectType);
            }

            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                var members = base.GetSerializableMembers(objectType);
                for (var type = objectType; type != null && type != typeof(object); type = type.BaseType)
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (!field.IsInitOnly && field.IsDefined(typeof(SerializeField), true) && !members.Contains(field))
                            members.Add(field);
                return members;
            }

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                // 以前JsonUtilityで保存した非公開フィールドも、同じフィールド名で読み戻す。
                if (member.IsDefined(typeof(SerializeField), true) && !member.IsDefined(typeof(JsonIgnoreAttribute), true) &&
                    !member.IsDefined(typeof(NonSerializedAttribute), true))
                {
                    property.Ignored = false;
                    property.Readable = true;
                    property.Writable = true;
                }
                return property;
            }
        }
    }
}
