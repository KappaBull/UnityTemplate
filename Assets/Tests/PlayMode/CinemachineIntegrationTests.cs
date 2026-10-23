using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;

namespace UnityTemplate.Tests.PlayMode
{
    public class CinemachineIntegrationTests
    {
        [Test]
        public void CameraPipelineTracksMovedFollowTarget()
        {
            var target = new GameObject("Test Follow Target");
            var cameraObject = new GameObject("Test Cinemachine Camera");
            try
            {
                var camera = cameraObject.AddComponent<CinemachineCamera>();
                cameraObject.AddComponent<CinemachineHardLockToTarget>().Damping = 0;
                camera.Follow = target.transform;
                target.transform.position = new Vector3(2, 3, 4);

                camera.InternalUpdateCameraState(Vector3.up, -1);

                Assert.That(Vector3.Distance(camera.State.RawPosition, target.transform.position),
                    Is.LessThan(0.001f));

                target.transform.position = new Vector3(-3, 5, 1);
                camera.InternalUpdateCameraState(Vector3.up, -1);

                Assert.That(Vector3.Distance(camera.State.RawPosition, target.transform.position),
                    Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(target);
            }
        }
    }
}
