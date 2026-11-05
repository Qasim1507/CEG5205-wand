using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class RobotMoveActionFeedback : ActionFeedback<RobotMoveFeedback>
    {
        public const string k_RosMessageName = "niryo_one_msgs/RobotMoveActionFeedback";
        public override string RosMessageName => k_RosMessageName;


        public RobotMoveActionFeedback() : base()
        {
            this.feedback = new RobotMoveFeedback();
        }

        public RobotMoveActionFeedback(HeaderMsg header, GoalStatusMsg status, RobotMoveFeedback feedback) : base(header, status)
        {
            this.feedback = feedback;
        }
        public static RobotMoveActionFeedback Deserialize(MessageDeserializer deserializer) => new RobotMoveActionFeedback(deserializer);

        RobotMoveActionFeedback(MessageDeserializer deserializer) : base(deserializer)
        {
            this.feedback = RobotMoveFeedback.Deserialize(deserializer);
        }
        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.header);
            serializer.Write(this.status);
            serializer.Write(this.feedback);
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
