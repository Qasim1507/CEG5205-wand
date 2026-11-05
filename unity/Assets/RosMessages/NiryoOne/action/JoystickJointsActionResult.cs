using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class JoystickJointsActionResult : ActionResult<JoystickJointsResult>
    {
        public const string k_RosMessageName = "niryo_one_msgs/JoystickJointsActionResult";
        public override string RosMessageName => k_RosMessageName;


        public JoystickJointsActionResult() : base()
        {
            this.result = new JoystickJointsResult();
        }

        public JoystickJointsActionResult(HeaderMsg header, GoalStatusMsg status, JoystickJointsResult result) : base(header, status)
        {
            this.result = result;
        }
        public static JoystickJointsActionResult Deserialize(MessageDeserializer deserializer) => new JoystickJointsActionResult(deserializer);

        JoystickJointsActionResult(MessageDeserializer deserializer) : base(deserializer)
        {
            this.result = JoystickJointsResult.Deserialize(deserializer);
        }
        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.header);
            serializer.Write(this.status);
            serializer.Write(this.result);
        }


#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod]
#endif
        public static void Register()
        {
            MessageRegistry.Register(k_RosMessageName, Deserialize);
        }
    }
}
