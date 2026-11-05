using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;


namespace RosMessageTypes.NiryoOne
{
    public class RobotMoveAction : Action<RobotMoveActionGoal, RobotMoveActionResult, RobotMoveActionFeedback, RobotMoveGoal, RobotMoveResult, RobotMoveFeedback>
    {
        public const string k_RosMessageName = "niryo_one_msgs/RobotMoveAction";
        public override string RosMessageName => k_RosMessageName;


        public RobotMoveAction() : base()
        {
            this.action_goal = new RobotMoveActionGoal();
            this.action_result = new RobotMoveActionResult();
            this.action_feedback = new RobotMoveActionFeedback();
        }

        public static RobotMoveAction Deserialize(MessageDeserializer deserializer) => new RobotMoveAction(deserializer);

        RobotMoveAction(MessageDeserializer deserializer)
        {
            this.action_goal = RobotMoveActionGoal.Deserialize(deserializer);
            this.action_result = RobotMoveActionResult.Deserialize(deserializer);
            this.action_feedback = RobotMoveActionFeedback.Deserialize(deserializer);
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.action_goal);
            serializer.Write(this.action_result);
            serializer.Write(this.action_feedback);
        }

    }
}
