using System.Collections;
using System.Collections.Generic;
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class WandSequenceValidation
{
    [UnityTest]
    public IEnumerator UdpAcknowledgesReadinessAndRejectsDuplicateCastIds()
    {
        var root = new GameObject("udp bridge test");
        var type = Type.GetType("WandSpellDemoController, Assembly-CSharp");
        var controller = root.AddComponent(type);
        type.GetField("targets").SetValue(controller, new Transform[0]);
        type.GetField("udpPort").SetValue(controller, 0);
        var wand = new GameObject("pose test wand").transform;
        wand.SetParent(root.transform);
        type.GetField("wand").SetValue(controller, wand);
        yield return null;
        var receiver = (System.Net.Sockets.UdpClient)type.GetField("receiver",
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);
        var port = ((System.Net.IPEndPoint)receiver.Client.LocalEndPoint).Port;
        using (var client = new System.Net.Sockets.UdpClient())
        {
            client.Connect("127.0.0.1", port);
            string[] packets = {
                "{\"type\":\"ping\",\"session\":\"test\"}",
                "{\"type\":\"cast\",\"session\":\"test\",\"id\":1,\"spell\":\"fire\",\"conf\":0.9}",
                "{\"type\":\"cast\",\"session\":\"test\",\"id\":1,\"spell\":\"fire\",\"conf\":0.9}" };
            string[] expected = { "choose_wand", "ignored_not_ready_busy_or_invalid", "duplicate" };
            for (int i = 0; i < packets.Length; i++)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(packets[i]);
                client.Send(bytes, bytes.Length);
                var deadline = Time.realtimeSinceStartup + 2f;
                while (client.Available == 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.Greater(client.Available, 0, "No Unity acknowledgement");
                var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                var reply = System.Text.Encoding.UTF8.GetString(client.Receive(ref endpoint));
                StringAssert.Contains(expected[i], reply);
                StringAssert.Contains("test", reply);
            }
            type.GetField("arenaReady", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, true);
            string[] poses = {
                "{\"type\":\"pose\",\"session\":\"test\",\"pose_id\":1,\"q\":[1,0,0,0]}",
                "{\"type\":\"pose\",\"session\":\"test\",\"pose_id\":2,\"q\":[0.7071068,0,0.7071068,0]}",
                "{\"type\":\"pose\",\"session\":\"test\",\"pose_id\":1,\"q\":[1,0,0,0]}" };
            foreach (var pose in poses)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(pose);
                client.Send(bytes, bytes.Length);
                yield return new WaitForSeconds(.2f);
            }
            Assert.Greater(Quaternion.Angle(Quaternion.identity, wand.rotation), 80f,
                "Pose must rotate the wand; stale pose must not reset it");
            Assert.AreEqual(2, type.GetField("poseCount", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller));
        }
        UnityEngine.Object.Destroy(root);
        yield return null;
    }

    [Test]
    public void AimingAcceptsOnlyCurrentCrystal()
    {
        var type = Type.GetType("WandSpellDemoController, Assembly-CSharp");
        var root = new GameObject("aim test");
        var controller = root.AddComponent(type);
        var cameraObject = new GameObject("aim camera");
        cameraObject.transform.SetParent(root.transform);
        cameraObject.transform.position = new Vector3(0f, 1f, 0f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 72f;
        camera.aspect = 16f / 9f;
        var targets = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            target.transform.SetParent(root.transform);
            target.transform.position = new Vector3(i * 2f, 1f, 3f);
            targets[i] = target.transform;
        }
        type.GetField("targets").SetValue(controller, targets);
        Physics.SyncTransforms();
        var method = type.GetMethod("TryGetAimedCrystal", BindingFlags.NonPublic | BindingFlags.Instance);
        var advance = type.GetMethod("AdvanceAim", BindingFlags.NonPublic | BindingFlags.Static);
        var secondAim = (Vector2)advance.Invoke(null,
            new object[] { new Vector2(.5f,.5f), new Vector2(172f,0f) });
        object[] hit0 = { camera.ViewportPointToRay(new Vector3(.5f,.5f,0f)), Vector3.zero };
        object[] hit1 = { camera.ViewportPointToRay(new Vector3(secondAim.x,secondAim.y,0f)), Vector3.zero };
        Assert.IsTrue((bool)method.Invoke(controller, hit0));
        Assert.IsFalse((bool)method.Invoke(controller, hit1));
        type.GetField("selected", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, 1);
        Assert.IsTrue((bool)method.Invoke(controller, hit1));
        UnityEngine.Object.DestroyImmediate(root);
    }

    [UnityTest]
    public IEnumerator FiveSpellsShatterInOrderAndReset()
    {
        var root = new GameObject("wand test root");
        var wand = new GameObject("wand").transform;
        wand.SetParent(root.transform);
        var tip = new GameObject("tip").transform;
        tip.SetParent(wand); tip.position = new Vector3(0f, 1f, -1f);
        var type = Type.GetType("WandSpellDemoController, Assembly-CSharp");
        Assert.IsNotNull(type);
        var controller = root.AddComponent(type);
        void Set(string name, object value) => type.GetField(name).SetValue(controller, value);
        Set("wand", wand); Set("wandTip", tip);
        var styles = new Transform[3];
        for (int i = 0; i < 3; i++) styles[i] = new GameObject("style " + i).transform;
        Set("wandStyles", styles);
        var targets = new Transform[5]; var renderers = new Renderer[5];
        for (int i = 0; i < 5; i++)
        {
            var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crystal.transform.SetParent(root.transform);
            crystal.transform.position = new Vector3(i, 1f, 1f);
            targets[i] = crystal.transform;
            renderers[i] = crystal.GetComponent<Renderer>();
        }
        Set("targets", targets); Set("targetCrystals", renderers);
        yield return null;
        type.GetMethod("SetWandStyle", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(controller, new object[] { 0 });
        var spells = new[] { "fire", "frost", "storm", "arcane", "shatter" };
        for (int i = 0; i < 5; i++)
        {
            type.GetMethod("CastSpell").Invoke(controller, new object[] { spells[i], 1f, "TEST" });
            yield return new WaitForSeconds(1.45f);
            Assert.IsFalse(targets[i].gameObject.activeSelf, "Crystal " + i + " did not shatter");
            if (i < 4) Assert.IsTrue(targets[i+1].gameObject.activeSelf,
                "Next crystal disappeared too soon");
        }
        yield return new WaitForSeconds(2.8f);
        foreach (var crystal in targets)
            Assert.IsTrue(crystal.gameObject.activeSelf, "Crystals did not reset");
        UnityEngine.Object.Destroy(root);
        foreach (var style in styles) if (style != null) UnityEngine.Object.Destroy(style.gameObject);
    }
}
