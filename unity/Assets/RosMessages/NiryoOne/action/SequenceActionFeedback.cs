using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class SequenceActionFeedback : ActionFeedback<SequenceFeedback>
    {
        public const string k_RosMessageName = "niryo_one_msgs/SequenceActionFeedback";
        public override string RosMessageName => k_RosMessageName;


        public SequenceActionFeedback() : base()
        {
            this.feedback = new SequenceFeedback();
        }

        public SequenceActionFeedback(HeaderMsg header, GoalStatusMsg status, SequenceFeedback feedback) : base(header, status)
        {
            this.feedback = feedback;
        }
        public static SequenceActionFeedback Deserialize(MessageDeserializer deserializer) => new SequenceActionFeedback(deserializer);

        SequenceActionFeedback(MessageDeserializer deserializer) : base(deserializer)
        {
            this.feedback = SequenceFeedback.Deserialize(deserializer);
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
