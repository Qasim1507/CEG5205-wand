using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Actionlib;

namespace RosMessageTypes.NiryoOne
{
    public class JoystickJointsActionGoal : ActionGoal<JoystickJointsGoal>
    {
        public const string k_RosMessageName = "niryo_one_msgs/JoystickJointsActionGoal";
        public override string RosMessageName => k_RosMessageName;


        public JoystickJointsActionGoal() : base()
        {
            this.goal = new JoystickJointsGoal();
        }

        public JoystickJointsActionGoal(HeaderMsg header, GoalIDMsg goal_id, JoystickJointsGoal goal) : base(header, goal_id)
        {
            this.goal = goal;
        }
        public static JoystickJointsActionGoal Deserialize(MessageDeserializer deserializer) => new JoystickJointsActionGoal(deserializer);

        JoystickJointsActionGoal(MessageDeserializer deserializer) : base(deserializer)
        {
            this.goal = JoystickJointsGoal.Deserialize(deserializer);
        }
        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.header);
            serializer.Write(this.goal_id);
            serializer.Write(this.goal);
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
