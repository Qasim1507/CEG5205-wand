using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;


namespace RosMessageTypes.NiryoOne
{
    public class JoystickJointsAction : Action<JoystickJointsActionGoal, JoystickJointsActionResult, JoystickJointsActionFeedback, JoystickJointsGoal, JoystickJointsResult, JoystickJointsFeedback>
    {
        public const string k_RosMessageName = "niryo_one_msgs/JoystickJointsAction";
        public override string RosMessageName => k_RosMessageName;


        public JoystickJointsAction() : base()
        {
            this.action_goal = new JoystickJointsActionGoal();
            this.action_result = new JoystickJointsActionResult();
            this.action_feedback = new JoystickJointsActionFeedback();
        }

        public static JoystickJointsAction Deserialize(MessageDeserializer deserializer) => new JoystickJointsAction(deserializer);

        JoystickJointsAction(MessageDeserializer deserializer)
        {
            this.action_goal = JoystickJointsActionGoal.Deserialize(deserializer);
            this.action_result = JoystickJointsActionResult.Deserialize(deserializer);
            this.action_feedback = JoystickJointsActionFeedback.Deserialize(deserializer);
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.action_goal);
            serializer.Write(this.action_result);
            serializer.Write(this.action_feedback);
        }

    }
}
