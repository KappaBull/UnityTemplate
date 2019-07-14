using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kappa.Editor
{
  public class SoundImporter : AssetPostprocessor
  {
    private void OnPreprocessAudio()
    {
      var checkList = new[]
      {
        "Assets/Audio/Action",
        "Assets/Audio/Bgm"
      };
      if (!checkList.Any(folder => assetPath.Contains(folder))) return;

      var audioImporter = assetImporter as AudioImporter;
      if (audioImporter == null) return;
      audioImporter.forceToMono = true;
      audioImporter.loadInBackground = true;
      audioImporter.preloadAudioData = false;
      if (assetPath.Contains("BGM"))
      {
        audioImporter.defaultSampleSettings = new AudioImporterSampleSettings()
        {
          loadType = AudioClipLoadType.Streaming,
          compressionFormat = AudioCompressionFormat.Vorbis,
          quality = 100,
          sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
        };
      }
      else
      {
        if (assetPath.Contains("Title.mp3"))
        {
          audioImporter.defaultSampleSettings = new AudioImporterSampleSettings()
          {
            loadType = AudioClipLoadType.Streaming,
            compressionFormat = AudioCompressionFormat.Vorbis,
            quality = 100,
            sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
          };
        }
        else
        {
          audioImporter.defaultSampleSettings = new AudioImporterSampleSettings()
          {
            loadType = AudioClipLoadType.CompressedInMemory,
            compressionFormat = AudioCompressionFormat.Vorbis,
            quality = 100,
            sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
          };
        }
      }

    }

  }
}