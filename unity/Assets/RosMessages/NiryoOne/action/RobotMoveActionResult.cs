using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class RobotMoveActionResult : ActionResult<RobotMoveResult>
    {
        public const string k_RosMessageName = "niryo_one_msgs/RobotMoveActionResult";
        public override string RosMessageName => k_RosMessageName;


        public RobotMoveActionResult() : base()
        {
            this.result = new RobotMoveResult();
        }

        public RobotMoveActionResult(HeaderMsg header, GoalStatusMsg status, RobotMoveResult result) : base(header, status)
        {
            this.result = result;
        }
        public static RobotMoveActionResult Deserialize(MessageDeserializer deserializer) => new RobotMoveActionResult(deserializer);

        RobotMoveActionResult(MessageDeserializer deserializer) : base(deserializer)
        {
            this.result = RobotMoveResult.Deserialize(deserializer);
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
