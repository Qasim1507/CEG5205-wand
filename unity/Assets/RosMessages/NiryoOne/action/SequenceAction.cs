using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;


namespace RosMessageTypes.NiryoOne
{
    public class SequenceAction : Action<SequenceActionGoal, SequenceActionResult, SequenceActionFeedback, SequenceGoal, SequenceResult, SequenceFeedback>
    {
        public const string k_RosMessageName = "niryo_one_msgs/SequenceAction";
        public override string RosMessageName => k_RosMessageName;


        public SequenceAction() : base()
        {
            this.action_goal = new SequenceActionGoal();
            this.action_result = new SequenceActionResult();
            this.action_feedback = new SequenceActionFeedback();
        }

        public static SequenceAction Deserialize(MessageDeserializer deserializer) => new SequenceAction(deserializer);

        SequenceAction(MessageDeserializer deserializer)
        {
            this.action_goal = SequenceActionGoal.Deserialize(deserializer);
            this.action_result = SequenceActionResult.Deserialize(deserializer);
            this.action_feedback = SequenceActionFeedback.Deserialize(deserializer);
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.action_goal);
            serializer.Write(this.action_result);
            serializer.Write(this.action_feedback);
        }

    }
}
