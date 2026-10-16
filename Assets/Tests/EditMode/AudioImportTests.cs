using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UnityTemplate.Tests.EditMode
{
    public class AudioImportTests
    {
        private readonly List<string> createdFolders = new List<string>();
        private string assetPath;

        [TearDown]
        public void Cleanup()
        {
            if (assetPath != null) AssetDatabase.DeleteAsset(assetPath);
            for (int i = createdFolders.Count - 1; i >= 0; i--)
            {
                string folder = createdFolders[i];
                if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0)
                    AssetDatabase.DeleteAsset(folder);
            }
            createdFolders.Clear();
            assetPath = null;
        }

        [TestCase("BGM", AudioClipLoadType.Streaming, true)]
        [TestCase("Bgm", AudioClipLoadType.Streaming, true)]
        [TestCase("Action", AudioClipLoadType.CompressedInMemory, true)]
        [TestCase("BgmOther", AudioClipLoadType.DecompressOnLoad, false)]
        public void ImportPolicyPreservesAudioSettings(string folder, AudioClipLoadType expectedLoadType, bool matched)
        {
            EnsureFolder("Assets", "Audio");
            EnsureFolder("Assets/Audio", folder);
            assetPath = "Assets/Audio/" + folder + "/ImportTest-" + Guid.NewGuid().ToString("N") + ".wav";
            WriteStereoWave(assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(assetPath);
            importer.forceToMono = false;
            importer.loadInBackground = false;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.37f;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 22050;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();

            importer = (AudioImporter)AssetImporter.GetAtPath(assetPath);
            settings = importer.defaultSampleSettings;
            Assert.That(importer.forceToMono, Is.False);
            Assert.That(importer.loadInBackground, Is.EqualTo(matched));
            Assert.That(settings.loadType, Is.EqualTo(expectedLoadType));
            Assert.That(settings.preloadAudioData, Is.EqualTo(!matched));
            Assert.That(settings.compressionFormat, Is.EqualTo(AudioCompressionFormat.Vorbis));
            Assert.That(settings.quality, Is.EqualTo(0.37f).Within(0.001f));
            Assert.That(settings.sampleRateSetting, Is.EqualTo(AudioSampleRateSetting.OverrideSampleRate));
            Assert.That(settings.sampleRateOverride, Is.EqualTo(22050));
            Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath).channels, Is.EqualTo(2));
        }

        private void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (AssetDatabase.IsValidFolder(path)) return;
            AssetDatabase.CreateFolder(parent, name);
            createdFolders.Add(path);
        }

        private static void WriteStereoWave(string path)
        {
            const int frames = 4410;
            const int dataBytes = frames * 2 * sizeof(short);
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)2);
                writer.Write(44100);
                writer.Write(44100 * 4);
                writer.Write((short)4);
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                for (int i = 0; i < frames; i++)
                {
                    writer.Write((short)(Math.Sin(i * 0.1) * 1000));
                    writer.Write((short)(Math.Cos(i * 0.1) * 1000));
                }
            }
        }
    }
}
