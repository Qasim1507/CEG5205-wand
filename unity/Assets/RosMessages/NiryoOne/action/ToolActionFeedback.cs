using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class ToolActionFeedback : ActionFeedback<ToolFeedback>
    {
        public const string k_RosMessageName = "niryo_one_msgs/ToolActionFeedback";
        public override string RosMessageName => k_RosMessageName;


        public ToolActionFeedback() : base()
        {
            this.feedback = new ToolFeedback();
        }

        public ToolActionFeedback(HeaderMsg header, GoalStatusMsg status, ToolFeedback feedback) : base(header, status)
        {
            this.feedback = feedback;
        }
        public static ToolActionFeedback Deserialize(MessageDeserializer deserializer) => new ToolActionFeedback(deserializer);

        ToolActionFeedback(MessageDeserializer deserializer) : base(deserializer)
        {
            this.feedback = ToolFeedback.Deserialize(deserializer);
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
