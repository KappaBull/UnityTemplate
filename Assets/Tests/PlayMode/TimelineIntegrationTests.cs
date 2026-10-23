using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace UnityTemplate.Tests.PlayMode
{
    public class TimelineIntegrationTests
    {
        [Test]
        public void ActivationTrackControlsBoundObjectAtEvaluatedTime()
        {
            var directorObject = new GameObject("Test Timeline Director");
            var target = new GameObject("Test Timeline Target");
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            try
            {
                target.SetActive(false);
                var track = timeline.CreateTrack<ActivationTrack>(null, "Activation");
                var clip = track.CreateDefaultClip();
                clip.start = 1;
                clip.duration = 2;
                var director = directorObject.AddComponent<PlayableDirector>();
                director.timeUpdateMode = DirectorUpdateMode.Manual;
                director.playableAsset = timeline;
                director.SetGenericBinding(track, target);

                director.time = 0;
                director.Evaluate();
                Assert.That(target.activeSelf, Is.False);

                director.time = 2;
                director.Evaluate();
                Assert.That(target.activeSelf, Is.True);

                director.time = 0;
                director.Evaluate();
                Assert.That(target.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(directorObject);
                foreach (var track in timeline.GetOutputTracks())
                {
                    foreach (var clip in track.GetClips())
                        Object.DestroyImmediate(clip.asset);
                    Object.DestroyImmediate(track);
                }
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(target);
            }
        }
    }
}
