using System.Collections;
using System.IO;
using FloorF.Harness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Harness
{
    /// <summary>
    /// L2 visual check: loads build scene 0, lets it run, writes captures/main.png.
    /// Run: powershell -File tools/unity-batch.ps1 -Mode playmode -Filter Capture
    /// Agents then open the PNG with the Read tool to look at it.
    /// </summary>
    public class CaptureTests
    {
        [UnityTest]
        public IEnumerator Capture_MainScene()
        {
            if (SceneManager.sceneCountInBuildSettings == 0)
                Assert.Ignore("No scenes in Build Settings yet.");

            yield return SceneManager.LoadSceneAsync(0);
            for (int i = 0; i < 60; i++) yield return null; // let bootstrap + first day spawn

            var path = FrameCapture.Capture("main");
            Assert.IsTrue(File.Exists(path), path);
        }
    }
}
