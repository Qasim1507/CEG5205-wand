using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class JoystickJointsActionFeedback : ActionFeedback<JoystickJointsFeedback>
    {
        public const string k_RosMessageName = "niryo_one_msgs/JoystickJointsActionFeedback";
        public override string RosMessageName => k_RosMessageName;


        public JoystickJointsActionFeedback() : base()
        {
            this.feedback = new JoystickJointsFeedback();
        }

        public JoystickJointsActionFeedback(HeaderMsg header, GoalStatusMsg status, JoystickJointsFeedback feedback) : base(header, status)
        {
            this.feedback = feedback;
        }
        public static JoystickJointsActionFeedback Deserialize(MessageDeserializer deserializer) => new JoystickJointsActionFeedback(deserializer);

        JoystickJointsActionFeedback(MessageDeserializer deserializer) : base(deserializer)
        {
            this.feedback = JoystickJointsFeedback.Deserialize(deserializer);
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
