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
    public IEnumerator MatchingSpellsShatterInOrderThenEnterDummyStage()
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
        var spells = new[] { "fire", "water", "wind", "lightning", "fire" };
        var crystalType = Type.GetType("CrystalTarget, Assembly-CSharp");
        for (int i = 0; i < 5; i++)
        {
            var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crystal.transform.SetParent(root.transform);
            crystal.transform.position = new Vector3(i, 1f, 1f);
            targets[i] = crystal.transform;
            renderers[i] = crystal.GetComponent<Renderer>();
            var requirement = crystal.AddComponent(crystalType);
            var required = crystalType.GetField("requiredSpell");
            required.SetValue(requirement, Enum.Parse(required.FieldType, spells[i], true));
        }
        Set("targets", targets); Set("targetCrystals", renderers);
        yield return null;
        var aimCamera = new GameObject("spell aim camera").AddComponent<Camera>();
        aimCamera.transform.SetParent(root.transform);
        aimCamera.transform.position = new Vector3(0f, 1f, -2f);
        type.GetField("viewCamera", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, aimCamera);
        type.GetMethod("SetWandStyle", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(controller, new object[] { 0 });
        var rig = new GameObject("level transition rig").transform;
        rig.SetParent(root.transform);
        Set("sceneRig", rig);
        aimCamera.transform.rotation = Quaternion.Euler(-70f, 0f, 0f);
        var missRay = aimCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
        Assert.IsTrue((bool)type.GetMethod("CastSpell").Invoke(controller, new object[] { "fire", 1f, "TEST MISS" }),
            "A miss should still produce a spell effect");
        // Moving the crosshair after firing must not turn this miss into a hit.
        aimCamera.transform.LookAt(targets[0]);
        yield return null;
        var effect = GameObject.Find("fire spell - temporary lines");
        Assert.IsNotNull(effect);
        var ribbon = effect.GetComponentInChildren<LineRenderer>();
        Assert.Less(Vector3.Distance(missRay.GetPoint(30f), ribbon.GetPosition(ribbon.positionCount - 1)), .02f,
            "The effect must end at the aimed point, not snap to a crystal");
        yield return new WaitForSeconds(.65f);
        Assert.IsTrue(targets[0].gameObject.activeSelf);
        Assert.AreEqual(0, type.GetField("score", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller));
        aimCamera.transform.LookAt(targets[1]);
        type.GetMethod("CastSpell").Invoke(controller, new object[] { "fire", 1f, "TEST WRONG TARGET" });
        yield return new WaitForSeconds(.7f);
        Assert.IsTrue(targets[0].gameObject.activeSelf);
        Assert.IsTrue(targets[1].gameObject.activeSelf);
        Assert.AreEqual(0, type.GetField("score", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller));
        aimCamera.transform.LookAt(targets[0]);
        type.GetMethod("CastSpell").Invoke(controller, new object[] { "water", 1f, "TEST WRONG ELEMENT" });
        yield return new WaitForSeconds(1.45f);
        Assert.IsTrue(targets[0].gameObject.activeSelf, "Wrong spell must not break a crystal");
        for (int i = 0; i < 5; i++)
        {
            aimCamera.transform.LookAt(targets[i]);
            type.GetMethod("CastSpell").Invoke(controller, new object[] { spells[i], 1f, "TEST" });
            yield return new WaitForSeconds(1.45f);
            Assert.IsFalse(targets[i].gameObject.activeSelf, "Crystal " + i + " did not shatter");
            if (i < 4) Assert.IsTrue(targets[i+1].gameObject.activeSelf,
                "Next crystal disappeared too soon");
        }
        yield return new WaitForSeconds(4.8f);
        Assert.IsTrue((bool)type.GetField("level2Active", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller),
            "Clearing the crystals must enter the new dummy stage");
        UnityEngine.Object.Destroy(root);
        foreach (var style in styles) if (style != null) UnityEngine.Object.Destroy(style.gameObject);
    }

    [Test]
    public void HeadAimTracksCameraAndIgnoresWandAndOldMouseOffset()
    {
        var root = new GameObject("head aiming test");
        var type = Type.GetType("WandSpellDemoController, Assembly-CSharp");
        var controller = root.AddComponent(type);
        var head = new GameObject("head").AddComponent<Camera>();
        head.transform.SetParent(root.transform);
        var target = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
        target.SetParent(root.transform);
        target.position = new Vector3(0, 0, 3);
        var reticle = new GameObject("crosshair").transform;
        reticle.SetParent(head.transform);
        var marker = new GameObject("hit marker").transform;
        marker.SetParent(root.transform);
        type.GetField("targets").SetValue(controller, new[] { target });
        type.GetField("crosshair").SetValue(controller, reticle);
        type.GetField("aimMarker").SetValue(controller, marker);
        type.GetField("viewCamera", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, head);
        type.GetField("aimViewport", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, new Vector2(.9f, .9f));
        var update = type.GetMethod("UpdateHeadAim", BindingFlags.NonPublic | BindingFlags.Instance);
        update.Invoke(controller, null);
        Assert.IsTrue(marker.gameObject.activeSelf, "Looking at the target should show its hit marker");
        Assert.AreEqual(new Vector3(0, 0, 1.2f), reticle.localPosition);
        head.transform.rotation = Quaternion.Euler(0, 80, 0);
        update.Invoke(controller, null);
        Assert.IsFalse(marker.gameObject.activeSelf, "Looking away must move the aim off target");
        UnityEngine.Object.DestroyImmediate(root);
    }

    [UnityTest]
    public IEnumerator ReadableHudShowsTheMotionForEachCurrentTarget()
    {
        var root = new GameObject("readable HUD test");
        var head = new GameObject("HUD camera").AddComponent<Camera>();
        head.tag = "MainCamera";
        head.transform.SetParent(root.transform);
        var type = Type.GetType("WandSpellDemoController, Assembly-CSharp");
        var controller = root.AddComponent(type);
        var crystalType = Type.GetType("CrystalTarget, Assembly-CSharp");
        var spells = new[] { "Fire", "Water", "Wind", "Lightning" };
        var expected = new[] { "top to bottom", "CLOCKWISE", "left to right", "down-left" };
        var targets = new Transform[4];
        for (int i = 0; i < targets.Length; i++) {
            targets[i] = new GameObject(spells[i]).transform;
            targets[i].SetParent(root.transform);
            var target = targets[i].gameObject.AddComponent(crystalType);
            var field = crystalType.GetField("requiredSpell");
            field.SetValue(target, Enum.Parse(field.FieldType, spells[i]));
        }
        type.GetField("targets").SetValue(controller, targets);
        type.GetField("udpPort").SetValue(controller, 0);
        var status = new GameObject("status").AddComponent<TextMesh>();
        status.transform.SetParent(root.transform);
        type.GetField("statusText").SetValue(controller, status);
        yield return null;
        type.GetMethod("SetWandStyle", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { 0 });
        var hint = (TextMesh)type.GetField("gestureText", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);
        Assert.GreaterOrEqual(Vector3.Distance(head.transform.position, status.transform.position), 3f);
        Assert.Less(Mathf.Abs(status.transform.position.y - head.transform.position.y), .5f,
            "Status must be near eye level, not above the player's view");
        var fixedPosition = hint.transform.position;
        var fixedRotation = hint.transform.rotation;
        head.transform.position += Vector3.right * .4f;
        head.transform.rotation = Quaternion.Euler(20f, 70f, 0f);
        for (int i = 0; i < targets.Length; i++) {
            type.GetField("selected", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, i);
            yield return null;
            StringAssert.Contains(expected[i], hint.text);
            var size = hint.GetComponent<Renderer>().localBounds.size;
            Assert.Less(size.x, 3.05f, "Gesture text should fit its background: " + size);
            Assert.Less(size.y, .65f, "Gesture text should fit its background: " + size);
            Assert.Less(Vector3.Distance(fixedPosition, hint.transform.position), .001f,
                "Instructions must stay fixed in the room as the head moves");
            Assert.Less(Quaternion.Angle(fixedRotation, hint.transform.rotation), .01f);
        }
        UnityEngine.Object.Destroy(root);
        yield return null;
    }
}
