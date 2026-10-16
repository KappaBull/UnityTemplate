using System;
using System.IO;
using UnityEngine;

namespace Kappa
{
    public enum SaveEnvironment
    {
        Production,
        Development,
        Beta
    }

    [CreateAssetMenu(menuName = "Kappa/Save Environment Settings")]
    public sealed class SaveEnvironmentSettings : ScriptableObject
    {
        public const string ResourceName = "SaveEnvironment";
        [SerializeField] private SaveEnvironment environment = SaveEnvironment.Production;

        public SaveEnvironment Environment => environment;
        public bool AllowsVersionDowngrade => environment == SaveEnvironment.Development;

        public static SaveEnvironmentSettings Current
        {
            get
            {
                var settings = Resources.Load<SaveEnvironmentSettings>(ResourceName);
                if (settings == null) throw new InvalidOperationException("SaveEnvironment settings are missing from Resources.");
                return settings;
            }
        }

        public static bool IsProductionPlayer
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return false;
#else
                return true;
#endif
            }
        }

        public void Validate(bool productionPlayer)
        {
            if (environment != SaveEnvironment.Production && environment != SaveEnvironment.Development && environment != SaveEnvironment.Beta)
                throw new InvalidOperationException("Unknown save environment.");
            if (productionPlayer && environment == SaveEnvironment.Development)
                throw new InvalidOperationException("Development save settings cannot be used in a production player.");
        }

        public string GetDefaultDirectory()
        {
            Validate(IsProductionPlayer);
            if (string.IsNullOrEmpty(Application.persistentDataPath))
                throw new NotSupportedException("Persistent storage is unavailable on this platform.");
            string root = Path.Combine(Application.persistentDataPath, "Saves");
            return GetDirectory(root, environment);
        }

        public static string GetDirectory(string root, SaveEnvironment channel)
        {
            switch (channel)
            {
                case SaveEnvironment.Production: return root;
                case SaveEnvironment.Development: return Path.Combine(root, "Development");
                case SaveEnvironment.Beta: return Path.Combine(root, "Beta");
                default: throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }
    }
}
