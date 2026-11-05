using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;


namespace RosMessageTypes.NiryoOne
{
    public class ToolAction : Action<ToolActionGoal, ToolActionResult, ToolActionFeedback, ToolGoal, ToolResult, ToolFeedback>
    {
        public const string k_RosMessageName = "niryo_one_msgs/ToolAction";
        public override string RosMessageName => k_RosMessageName;


        public ToolAction() : base()
        {
            this.action_goal = new ToolActionGoal();
            this.action_result = new ToolActionResult();
            this.action_feedback = new ToolActionFeedback();
        }

        public static ToolAction Deserialize(MessageDeserializer deserializer) => new ToolAction(deserializer);

        ToolAction(MessageDeserializer deserializer)
        {
            this.action_goal = ToolActionGoal.Deserialize(deserializer);
            this.action_result = ToolActionResult.Deserialize(deserializer);
            this.action_feedback = ToolActionFeedback.Deserialize(deserializer);
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.action_goal);
            serializer.Write(this.action_result);
            serializer.Write(this.action_feedback);
        }

    }
}
