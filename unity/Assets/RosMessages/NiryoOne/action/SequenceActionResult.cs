using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class SequenceActionResult : ActionResult<SequenceResult>
    {
        public const string k_RosMessageName = "niryo_one_msgs/SequenceActionResult";
        public override string RosMessageName => k_RosMessageName;


        public SequenceActionResult() : base()
        {
            this.result = new SequenceResult();
        }

        public SequenceActionResult(HeaderMsg header, GoalStatusMsg status, SequenceResult result) : base(header, status)
        {
            this.result = result;
        }
        public static SequenceActionResult Deserialize(MessageDeserializer deserializer) => new SequenceActionResult(deserializer);

        SequenceActionResult(MessageDeserializer deserializer) : base(deserializer)
        {
            this.result = SequenceResult.Deserialize(deserializer);
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
