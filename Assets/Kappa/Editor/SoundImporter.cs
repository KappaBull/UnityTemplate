using System;
using UnityEditor;
using UnityEngine;

namespace Kappa.Editor
{
    public class SoundImporter : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            bool isBgm = assetPath.StartsWith("Assets/Audio/BGM/", StringComparison.OrdinalIgnoreCase);
            bool isAction = assetPath.StartsWith("Assets/Audio/Action/", StringComparison.OrdinalIgnoreCase);
            if (!isBgm && !isAction) return;

            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.loadType = isBgm ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = true;
        }
    }
}
