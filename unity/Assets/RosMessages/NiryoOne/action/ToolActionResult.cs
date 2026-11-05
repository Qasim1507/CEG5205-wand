using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class ToolActionResult : ActionResult<ToolResult>
    {
        public const string k_RosMessageName = "niryo_one_msgs/ToolActionResult";
        public override string RosMessageName => k_RosMessageName;


        public ToolActionResult() : base()
        {
            this.result = new ToolResult();
        }

        public ToolActionResult(HeaderMsg header, GoalStatusMsg status, ToolResult result) : base(header, status)
        {
            this.result = result;
        }
        public static ToolActionResult Deserialize(MessageDeserializer deserializer) => new ToolActionResult(deserializer);

        ToolActionResult(MessageDeserializer deserializer) : base(deserializer)
        {
            this.result = ToolResult.Deserialize(deserializer);
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
